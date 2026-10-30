using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;
using MSharpEject.Audit;
using MSharpEject.Eject;

namespace MSharpEject.DeOlive;

public record DocumentRewrite(string NewText, Dictionary<string, int> Applied, int SkippedInQueries);

public static class DeOliver
{
    public static async Task<RunOutcome> RunAsync(string repo, RunOptions options, RunLog log, CancellationToken cancel)
    {
        repo = Path.GetFullPath(repo);
        string Rel(string f) => Path.GetRelativePath(repo, f).Replace('\\', '/');
        log.Info($"Replacing Olive in {repo}{(options.DryRun ? " (dry run)" : "")}");
        var notes = new List<string>();

        if (new[] { "M#", "@M#" }.Any(d => Directory.Exists(Path.Combine(repo, d))))
            return Stop("This repository still has M#. Run eject first: M# would regenerate code over these changes, and generated code isn't source yet.");
        if (!options.DryRun && !options.AllowDirty)
        {
            log.Progress(0, "Checking git status");
            if (await Git.WhyNotCleanAsync(repo, log, cancel) is { } dirty) return Stop(dirty);
        }

        var projects = RepoScan.RepoFiles(repo)
            .Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && RepoScan.IsCodeProject(Rel(f)))
            .OrderBy(Rel, StringComparer.Ordinal).ToList();
        using var loaded = await ProjectLoader.LoadAsync(repo, projects, log, 0.02, 0.35, cancel);
        notes.AddRange(loaded.Errors.Select(e => "Load problem: " + e));

        var changes = new List<Change>();
        var totals = new Dictionary<string, int>();
        var skipped = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = loaded.Projects
            .Where(p => !RepoScan.IsGeeksPackage(p.AssemblyName))
            .SelectMany(p => p.Documents)
            .Where(d => d.FilePath != null && File.Exists(d.FilePath) && !Rel(d.FilePath).Split('/').Any(s => s is "obj" or "bin") && seen.Add(d.FilePath))
            .ToList();

        // Semantic models are safe to query concurrently; each file is rewritten independently.
        var rewrites = new System.Collections.Concurrent.ConcurrentBag<(Document Document, DocumentRewrite Rewrite)>();
        var done = 0;
        await Parallel.ForEachAsync(documents, new ParallelOptions { CancellationToken = cancel }, async (document, token) =>
        {
            if (await RewriteAsync(document, token) is { } rewrite) rewrites.Add((document, rewrite));
            var count = Interlocked.Increment(ref done);
            if (count % 25 == 0) log.Progress(0.35 + 0.1 * count / documents.Count, $"Rewriting Olive calls ({count}/{documents.Count} files)");
        });

        foreach (var (document, rewrite) in rewrites.OrderBy(r => r.Document.FilePath, StringComparer.Ordinal))
        {
            skipped += rewrite.SkippedInQueries;
            if (rewrite.Applied.Count == 0) continue;
            foreach (var (method, count) in rewrite.Applied) totals[method] = totals.GetValueOrDefault(method) + count;
            var (_, encoding) = FileText.Read(document.FilePath!);
            var summary = string.Join(", ", rewrite.Applied.OrderBy(a => a.Key).Select(a => $"{a.Key} ×{a.Value}"));
            changes.Add(new EditFile(Rel(document.FilePath!), "Olive → .NET: " + summary, rewrite.NewText, encoding));
        }

        log.Info($"{changes.Count} files to change: " + string.Join(", ", totals.OrderByDescending(t => t.Value).Select(t => $"{t.Key} ×{t.Value}")));
        if (skipped > 0)
        {
            var note = $"{skipped} Olive calls inside query expressions were left alone: Olive translates them to SQL. They move with the data layer to EF Core.";
            log.Info(note);
            notes.Add(note);
        }
        if (changes.Count == 0)
        {
            log.Progress(1, "Nothing to rewrite");
            return new RunOutcome([], false, null, null, null, notes);
        }
        if (options.DryRun)
        {
            log.Progress(1, $"Dry run: {changes.Count} files would change");
            return new RunOutcome(changes, false, null, null, null, notes);
        }

        return await Ejector.ApplyAndVerifyAsync(repo, changes, () => ChangeApplier.Apply(repo, changes, log), options, log, notes, cancel);

        RunOutcome Stop(string reason)
        {
            log.Error(reason);
            return new RunOutcome([], false, null, null, reason, notes);
        }
    }

    public static async Task<DocumentRewrite?> RewriteAsync(Document document, CancellationToken cancel)
    {
        if (await document.GetSemanticModelAsync(cancel) is not { } model || await document.GetSyntaxRootAsync(cancel) is not { } root) return null;

        var rewriter = new OliveBclRewriter(model);
        var newRoot = rewriter.Visit(root);
        if (rewriter.Applied.Count == 0) return new DocumentRewrite("", [], rewriter.SkippedInQueries);

        var changed = await Simplifier.ReduceAsync(document.WithSyntaxRoot(newRoot), Simplifier.Annotation, cancellationToken: cancel);
        changed = await FixUsingsAsync(changed, rewriter.NeedsLinq, cancel);
        return new DocumentRewrite((await changed.GetTextAsync(cancel)).ToString(), rewriter.Applied, rewriter.SkippedInQueries);
    }

    static async Task<Document> FixUsingsAsync(Document document, bool needsLinq, CancellationToken cancel)
    {
        var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync(cancel))!;
        var model = (await document.GetSemanticModelAsync(cancel))!;
        var newLine = root.DescendantTrivia().FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia)).ToString() is { Length: > 0 } nl ? nl : "\n";

        // Drop `using Olive;` once nothing in the file binds to the Olive namespace.
        var oliveUsing = root.Usings.FirstOrDefault(u => u.Name?.ToString() == "Olive" && u.Alias == null && u.StaticKeyword == default);
        if (oliveUsing != null && !UsesOliveNamespace(root, model, cancel))
            root = root.RemoveNode(oliveUsing, SyntaxRemoveOptions.KeepNoTrivia)!;

        if (needsLinq && !HasLinq(root, model.Compilation))
        {
            var linq = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Linq"))
                .NormalizeWhitespace()
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(newLine));
            root = root.AddUsings(linq);
        }

        return document.WithSyntaxRoot(root);
    }

    static bool UsesOliveNamespace(CompilationUnitSyntax root, SemanticModel model, CancellationToken cancel) =>
        root.DescendantNodes(n => n is not UsingDirectiveSyntax).OfType<SimpleNameSyntax>().Any(name =>
        {
            var info = model.GetSymbolInfo(name, cancel);
            var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
            if (symbol is IMethodSymbol { ReducedFrom: { } reduced }) symbol = reduced;
            return symbol?.ContainingNamespace?.ToDisplayString() == "Olive";
        });

    static bool HasLinq(CompilationUnitSyntax root, Compilation compilation) =>
        root.Usings.Any(u => u.Name?.ToString() == "System.Linq")
        || compilation.SyntaxTrees.Any(t => t.GetRoot() is CompilationUnitSyntax c
            && c.Usings.Any(u => u.GlobalKeyword != default && u.Name?.ToString().Replace("global::", "") == "System.Linq"));
}
