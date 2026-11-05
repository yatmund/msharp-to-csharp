using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MSharpEject.Eject;

namespace MSharpEject.Replacement;

/// <param name="Final">The input solution with moved files rewritten; diff its documents against disk for the C# edits.</param>
/// <param name="Changes">Everything else: library files, project and solution edits, Razor, deletions.</param>
/// <param name="Gaps">Olive members the library doesn't provide yet, with use counts, most used first.</param>
public record SwapResult(Solution Final, List<Change> Changes, List<string> Gaps, List<string> Notes, HashSet<string> MovedFiles, bool OliveRemoved);

/// <summary>
/// Moves code from Olive to the replacement library, as far as the library reaches. A file that only calls Olive's static
/// helpers moves on its own once they all exist. Types can't be half-swapped, so the rest moves together, with the package
/// references, once every Olive member the repo uses exists. An in-memory compile decides; nothing moves that adds an error.
/// </summary>
public static class LibrarySwap
{
    public static async Task<SwapResult> PlanAsync(string repo, Solution solution, Func<Document, bool> inRepo, RunLog log, CancellationToken cancel)
    {
        string Rel(string f) => Path.GetRelativePath(repo, f).Replace('\\', '/');
        var notes = new List<string>();
        var nothing = new SwapResult(solution, [], [], notes, [], false);

        var users = solution.Projects.Where(p => !ReplacementLibrary.IsOliveAssembly(p.AssemblyName) && p.AssemblyName != ReplacementLibrary.Root && OliveDependencies(solution, p).Any()).ToList();
        if (users.Count == 0) return nothing;

        var available = users.SelectMany(p => p.MetadataReferences).OfType<PortableExecutableReference>()
            .Select(r => Path.GetFileNameWithoutExtension(r.FilePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var modules = ReplacementLibrary.For(users.SelectMany(p => OliveDependencies(solution, p)))
            .Where(m => m.NeedsReferences == null || m.NeedsReferences.All(available.Contains)).ToList();
        var sources = ReplacementLibrary.Sources(modules);
        var (work, libraryId) = WithLibrary(repo, solution, users, sources);
        var library = (await work.GetProject(libraryId)!.GetCompilationAsync(cancel))!;
        var libraryErrors = library.GetDiagnostics(cancel).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (libraryErrors.Count > 0)
        {
            foreach (var e in libraryErrors.Take(50)) log.Detail("Library: " + e);
            notes.Add($"The replacement library doesn't compile against this repo's references ({libraryErrors.Count} errors, first: {libraryErrors[0].GetMessage()}). Olive stays.");
            return nothing;
        }

        var baseline = await ErrorsAsync(solution, cancel);
        var files = new List<FileUse>();
        foreach (var document in users.SelectMany(p => work.GetProject(p.Id)!.Documents).Where(inRepo))
            files.Add(await UsesAsync(document, library, cancel));

        var uncovered = files.SelectMany(f => f.Uncovered).GroupBy(s => s).Select(g => (Name: g.Key, Count: g.Count())).OrderByDescending(g => g.Count).ThenBy(g => g.Name, StringComparer.Ordinal).ToList();
        var gaps = uncovered.Select(g => $"{g.Name} ×{g.Count}").ToList();
        log.Detail($"Replacement library: {files.Count(f => f.Uses > 0)} files use Olive, {uncovered.Count} distinct members not provided yet");

        // Pass 1: helper-only files, one by one. A file that breaks the build goes back.
        var candidates = files.Where(f => f.Uses > 0 && f.Uncovered.Count == 0 && f.HelpersOnly).Select(f => f.Id).ToHashSet();
        var moved = new HashSet<DocumentId>();
        for (var round = 0; round < 6 && candidates.Count > 0; round++)
        {
            var trial = await RewriteAsync(work, candidates, cancel);
            var broken = await NewErrorsAsync(trial, baseline, libraryId, cancel);
            if (broken.Count == 0)
            {
                work = trial;
                moved.UnionWith(candidates);
                break;
            }
            var bad = broken.Select(b => b.Document).OfType<DocumentId>().Where(candidates.Contains).ToHashSet();
            foreach (var b in broken.Take(20)) log.Detail("Swap would break: " + b.Message);
            if (bad.Count == 0) break;
            candidates.ExceptWith(bad);
        }

        // Pass 2: everything else and the package references, all at once.
        var removed = false;
        if (uncovered.Count == 0)
        {
            var rest = files.Where(f => !moved.Contains(f.Id)).Select(f => f.Id).ToHashSet();
            var trial = WithoutOlive(await RewriteAsync(work, rest, cancel), users);
            var broken = await NewErrorsAsync(trial, baseline, libraryId, cancel);
            if (broken.Count == 0)
            {
                work = trial;
                moved.UnionWith(rest);
                removed = true;
            }
            else
            {
                foreach (var b in broken.Take(50)) log.Detail("Removing Olive would break: " + b.Message);
                gaps.AddRange(broken.Select(b => b.Message).Distinct().Take(50));
                notes.Add($"Every Olive member used has a replacement, but removing Olive still breaks the build ({broken.Count} errors, first: {broken[0].Message}).");
            }
        }

        var movedPaths = moved.Select(id => work.GetDocument(id)?.FilePath).OfType<string>().Select(Rel).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (movedPaths.Count == 0 && !removed) return new SwapResult(solution, [], gaps, notes, [], false);

        var changes = LibraryFiles(repo, users, modules, sources, removed);
        if (removed) changes.AddRange(AfterOlive(repo, solution, users, notes));
        return new SwapResult(WithoutLibrary(work, libraryId, solution), changes, gaps, notes, movedPaths, removed);
    }

    static IEnumerable<string> OliveDependencies(Solution solution, Project project) =>
        project.MetadataReferences.OfType<PortableExecutableReference>().Select(r => Path.GetFileNameWithoutExtension(r.FilePath))
            .Concat(project.ProjectReferences.Select(r => solution.GetProject(r.ProjectId)?.AssemblyName))
            .Where(ReplacementLibrary.IsOliveAssembly)
            .OfType<string>();

    static bool IsOliveReference(MetadataReference r) =>
        r is PortableExecutableReference pe && ReplacementLibrary.IsOliveAssembly(Path.GetFileNameWithoutExtension(pe.FilePath));

    static (Solution, ProjectId) WithLibrary(string repo, Solution solution, List<Project> users, List<(string Path, string Text)> sources)
    {
        var template = users.OrderByDescending(p => p.MetadataReferences.Count).First();
        var existing = solution.Projects.FirstOrDefault(p => p.AssemblyName == ReplacementLibrary.Root);
        ProjectId id;
        if (existing != null)
        {
            id = existing.Id;
            solution = existing.Documents.Aggregate(solution, (s, d) => s.RemoveDocument(d.Id));
        }
        else
        {
            id = ProjectId.CreateNewId();
            solution = solution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), ReplacementLibrary.Root, ReplacementLibrary.Root, LanguageNames.CSharp,
                filePath: Path.Combine(repo, ReplacementLibrary.ProjectPath),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: (template.ParseOptions as CSharpParseOptions ?? CSharpParseOptions.Default).WithLanguageVersion(LanguageVersion.Latest),
                metadataReferences: template.MetadataReferences.Where(r => !IsOliveReference(r))));
        }
        foreach (var (path, text) in sources)
            solution = solution.AddDocument(DocumentId.CreateNewId(id), Path.GetFileName(path), text, filePath: Path.Combine(repo, path));
        foreach (var user in users.Where(u => u.ProjectReferences.All(r => r.ProjectId != id)))
            solution = solution.AddProjectReference(user.Id, new ProjectReference(id));
        return (solution, id);
    }

    static Solution WithoutOlive(Solution solution, List<Project> users)
    {
        foreach (var user in users)
        {
            var project = solution.GetProject(user.Id)!;
            foreach (var r in project.MetadataReferences.Where(IsOliveReference)) solution = solution.RemoveMetadataReference(user.Id, r);
            foreach (var r in project.ProjectReferences.Where(r => ReplacementLibrary.IsOliveAssembly(solution.GetProject(r.ProjectId)?.AssemblyName)))
                solution = solution.RemoveProjectReference(user.Id, r);
        }
        return solution;
    }

    // Edits are diffed against disk, so the in-memory library project must not leak into the result.
    static Solution WithoutLibrary(Solution work, ProjectId libraryId, Solution original) =>
        original.GetProject(libraryId) != null ? work : work.RemoveProject(libraryId);

    record FileUse(DocumentId Id, int Uses, bool HelpersOnly, List<string> Uncovered);

    static async Task<FileUse> UsesAsync(Document document, Compilation library, CancellationToken cancel)
    {
        var model = (await document.GetSemanticModelAsync(cancel))!;
        var root = (await document.GetSyntaxRootAsync(cancel))!;
        var uses = 0;
        var helpersOnly = true;
        var uncovered = new List<string>();
        foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            var info = model.GetSymbolInfo(name, cancel);
            var symbol = Normalise(info.Symbol ?? info.CandidateSymbols.FirstOrDefault());
            if (symbol == null) continue;
            if (symbol is INamespaceSymbol ns)
            {
                if (ns.ToDisplayString().Split('.')[0] == "Olive") uses++;
                continue;
            }
            if (!ReplacementLibrary.IsOliveAssembly(symbol.ContainingAssembly?.Name)) continue;
            uses++;
            helpersOnly &= IsHelper(symbol);
            if (ReplacementLibrary.MappedId(symbol) is not { } id || DocumentationCommentId.GetFirstSymbolForDeclarationId(id, library) == null)
                uncovered.Add(symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat));
        }
        return new FileUse(document.Id, uses, helpersOnly, uncovered);
    }

    static ISymbol? Normalise(ISymbol? symbol) => symbol switch
    {
        null => null,
        IMethodSymbol { MethodKind: MethodKind.Constructor } c => c.ContainingType.OriginalDefinition,
        IMethodSymbol m => (m.ReducedFrom ?? m).OriginalDefinition,
        _ => symbol.OriginalDefinition,
    };

    // Static members of static classes carry no identity across files, so a file using only those can move alone.
    static bool IsHelper(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol t => t.IsStatic,
        IMethodSymbol or IPropertySymbol or IFieldSymbol => symbol.IsStatic && symbol.ContainingType is { IsStatic: true },
        _ => false,
    };

    static async Task<Solution> RewriteAsync(Solution solution, IEnumerable<DocumentId> ids, CancellationToken cancel)
    {
        var roots = new List<(DocumentId, SyntaxNode)>();
        foreach (var id in ids)
        {
            var document = solution.GetDocument(id)!;
            var rewriter = new OliveNamespaceRewriter((await document.GetSemanticModelAsync(cancel))!);
            var root = rewriter.Visit((await document.GetSyntaxRootAsync(cancel))!);
            if (rewriter.Renamed > 0) roots.Add((id, root));
        }
        return roots.Aggregate(solution, (s, r) => s.WithDocumentSyntaxRoot(r.Item1, r.Item2));
    }

    static string Key(Diagnostic d) =>
        $"{d.Id}|{d.Location.SourceTree?.FilePath}|{d.Location.GetLineSpan().StartLinePosition.Line}";

    static async Task<Dictionary<ProjectId, HashSet<string>>> ErrorsAsync(Solution solution, CancellationToken cancel)
    {
        var errors = new Dictionary<ProjectId, HashSet<string>>();
        foreach (var project in solution.Projects)
            if (await project.GetCompilationAsync(cancel) is { } c)
                errors[project.Id] = c.GetDiagnostics(cancel).Where(d => d.Severity == DiagnosticSeverity.Error).Select(Key).ToHashSet();
        return errors;
    }

    record Broken(DocumentId? Document, string Message);

    static async Task<List<Broken>> NewErrorsAsync(Solution trial, Dictionary<ProjectId, HashSet<string>> baseline, ProjectId libraryId, CancellationToken cancel)
    {
        var broken = new List<Broken>();
        foreach (var project in trial.Projects.Where(p => p.Id != libraryId))
        {
            if (await project.GetCompilationAsync(cancel) is not { } c) continue;
            var before = baseline.GetValueOrDefault(project.Id) ?? [];
            foreach (var d in c.GetDiagnostics(cancel).Where(d => d.Severity == DiagnosticSeverity.Error && !before.Contains(Key(d))))
            {
                var path = d.Location.SourceTree?.FilePath;
                var document = path == null ? null : trial.GetDocumentIdsWithFilePath(path).FirstOrDefault(id => id.ProjectId == project.Id);
                broken.Add(new Broken(document, $"{Path.GetFileName(path)}({d.Location.GetLineSpan().StartLinePosition.Line + 1}): {d.Id} {d.GetMessage()}"));
            }
        }
        return broken;
    }

    static List<Change> LibraryFiles(string repo, List<Project> users, List<LibraryModule> modules, List<(string Path, string Text)> sources, bool dropOlive)
    {
        var changes = new List<Change>();
        var library = Path.Combine(repo, ReplacementLibrary.ProjectPath);
        foreach (var (path, text) in sources)
        {
            var full = Path.Combine(repo, path);
            if (!File.Exists(full) || File.ReadAllText(full) != text)
                changes.Add(new EditFile(path, "Replacement for Olive, written for this repo", text, new System.Text.UTF8Encoding(false)));
        }
        if (!File.Exists(library))
        {
            var framework = users.Select(u => u.FilePath).OfType<string>().Where(File.Exists)
                .Select(f => Regex.Match(File.ReadAllText(f), @"<TargetFrameworks?>\s*([^;<\s]+)")).Where(m => m.Success)
                .Select(m => m.Groups[1].Value).OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault() ?? "net8.0";
            changes.Add(new EditFile(ReplacementLibrary.ProjectPath, "Project for the replacement library", ReplacementLibrary.Csproj(framework, modules), new System.Text.UTF8Encoding(false)));
        }

        foreach (var csproj in users.Select(u => u.FilePath).OfType<string>().Where(File.Exists))
        {
            var (xml, encoding) = FileText.Read(csproj);
            var relative = Path.GetRelativePath(Path.GetDirectoryName(csproj)!, library);
            if (EjectTransforms.OliveReferences(xml, relative, dropOlive) is { } edited)
                changes.Add(new EditFile(Path.GetRelativePath(repo, csproj).Replace('\\', '/'), dropOlive ? "Reference the replacement library in place of Olive" : "Reference the replacement library", edited, encoding));
        }

        foreach (var sln in Directory.GetFiles(repo, "*.sln").Concat(Directory.GetFiles(repo, "*.slnx")))
        {
            var (text, encoding) = FileText.Read(sln);
            var added = sln.EndsWith(".slnx") ? EjectTransforms.AddToSlnx(text, ReplacementLibrary.ProjectPath) : EjectTransforms.AddToSln(text, ReplacementLibrary.ProjectPath, Guid.NewGuid());
            var final = added ?? text;
            if (dropOlive) final = (sln.EndsWith(".slnx") ? EjectTransforms.RemoveOliveFromSlnx(final) : EjectTransforms.RemoveOliveFromSln(final)) ?? final;
            if (final != text) changes.Add(new EditFile(Path.GetFileName(sln), "Add the replacement library" + (dropOlive ? ", drop Olive projects" : ""), final, encoding));
        }
        return changes;
    }

    static IEnumerable<Change> AfterOlive(string repo, Solution solution, List<Project> users, List<string> notes)
    {
        string Rel(string f) => Path.GetRelativePath(repo, f).Replace('\\', '/');
        var dirs = users.Select(u => Path.GetDirectoryName(u.FilePath)).OfType<string>().Distinct().ToList();
        foreach (var file in dirs.SelectMany(d => Audit.RepoScan.RepoFiles(d)).Where(f => f.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)))
        {
            var (text, encoding) = FileText.Read(file);
            if (OliveNamespaceRewriter.Razor(text) is { } razor) yield return new EditFile(Rel(file), "Razor: Olive → replacement library", razor, encoding);
        }

        foreach (var change in dirs.SelectMany(d => FrontEndSwap.Plan(repo, d))) yield return change;

        foreach (var vendored in solution.Projects.Where(p => ReplacementLibrary.IsOliveAssembly(p.AssemblyName) && p.FilePath != null))
            yield return new DeleteDirectory(Rel(Path.GetDirectoryName(vendored.FilePath!)!), "Vendored Olive source, no longer referenced");

        var config = Audit.RepoScan.RepoFiles(repo).Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.ReadAllText(f).Contains("\"Olive.")).Select(Rel).ToList();
        if (config.Count > 0) notes.Add("Config still names Olive types, check these by hand: " + string.Join(", ", config));
    }
}
