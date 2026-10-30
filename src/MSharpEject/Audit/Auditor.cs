using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace MSharpEject.Audit;

public record AuditResult(
    string Repo,
    List<PackageRef> Packages,
    List<Finding> BuildSteps,
    Dictionary<string, int> Dsl,
    List<GeneratedFolder> Generated,
    DateTime? NewestDslWrite,
    List<Finding> Blockers,
    bool Semantic,
    List<SymbolUse> Symbols,
    List<string> LoadErrors);

public static class Auditor
{
    public static async Task<AuditResult> RunAsync(string repo, bool semantic, Action<string> log)
    {
        repo = Path.GetFullPath(repo);
        var files = RepoScan.RepoFiles(repo).ToList();
        string Rel(string f) => Path.GetRelativePath(repo, f).Replace('\\', '/');

        var projects = files.Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).ToList();
        var packages = projects.SelectMany(p => RepoScan.GeeksPackages(Rel(p), File.ReadAllText(p)))
            .Concat(files.Where(f => Path.GetFileName(f) == "dotnet-tools.json").SelectMany(f => RepoScan.GeeksTools(Rel(f), File.ReadAllText(f))))
            .Concat(files.Where(f => Path.GetFileName(f) == "package.json").SelectMany(f => RepoScan.GeeksNpmPackages(Rel(f), File.ReadAllText(f))))
            .ToList();
        var buildSteps = projects.SelectMany(p => RepoScan.BuildSteps(Rel(p), File.ReadAllText(p))).ToList();

        var dslFiles = files.Where(f => RepoScan.IsDslFile(Rel(f)) && f.EndsWith(".cs")).ToList();
        var dsl = RepoScan.DslInventory(dslFiles.Select(File.ReadAllText));
        var generated = RepoScan.GeneratedFolderPaths.Select(g => RepoScan.ScanGeneratedFolder(repo, g)).ToList();

        var blockers = files
            .Where(f => !RepoScan.IsDslFile(Rel(f)) && !RepoScan.IsGeneratedFile(Rel(f)))
            .Where(f => new FileInfo(f).Length < 2_000_000)
            .SelectMany(f => RepoScan.DeploymentBlockers(Rel(f), File.ReadAllText(f)))
            .ToList();

        var symbols = new List<SymbolUse>();
        var loadErrors = new List<string>();
        if (semantic)
            (symbols, loadErrors) = await CollectSymbolsAsync(repo, projects, Rel, log);

        return new AuditResult(repo, packages, buildSteps, dsl, generated,
            dslFiles.Count == 0 ? null : dslFiles.Max(File.GetLastWriteTimeUtc), blockers, semantic, symbols, loadErrors);
    }

    static async Task<(List<SymbolUse>, List<string>)> CollectSymbolsAsync(string repo, List<string> projects, Func<string, string> rel, Action<string> log)
    {
        // DSL projects (#Model, #UI) get deleted, their MSharp usage doesn't matter.
        var targets = projects.Where(p => !Path.GetFileName(p).StartsWith('#') && !RepoScan.IsDslFile(rel(p))).ToList();

        foreach (var project in targets)
        {
            log($"Restoring {rel(project)}...");
            await Run("dotnet", $"restore \"{project}\"", repo, log);
        }

        using var workspace = MSBuildWorkspace.Create();
        var errors = new List<string>();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure) errors.Add(e.Diagnostic.Message);
        });

        var compilations = new List<Compilation>();
        foreach (var path in targets)
        {
            log($"Loading {rel(path)}...");
            var project = workspace.CurrentSolution.Projects.FirstOrDefault(p => p.FilePath == path)
                ?? await workspace.OpenProjectAsync(path);
            if (await project.GetCompilationAsync() is { } compilation) compilations.Add(compilation);
        }

        log("Resolving Olive symbols...");
        return (OliveUsage.Collect(compilations, f => RepoScan.IsGeneratedFile(rel(f))), errors);
    }

    static async Task Run(string exe, string args, string cwd, Action<string> log)
    {
        using var process = Process.Start(new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) log($"  {exe} {args} failed:\n{await output}{await error}");
    }

    public static string ToMarkdown(AuditResult r)
    {
        var md = new StringBuilder();
        md.AppendLine($"# M# / Olive audit: {Path.GetFileName(r.Repo)}").AppendLine();

        md.AppendLine("## Geeks packages and tools").AppendLine();
        Table(md, ["Project", "Package", "Version"], r.Packages.OrderBy(p => p.Id).Select(p => new[] { p.Project, p.Id, p.Version ?? "" }));

        md.AppendLine("## M# definitions").AppendLine();
        Table(md, ["Kind", "Count"], r.Dsl.OrderByDescending(d => d.Value).Select(d => new[] { d.Key, d.Value.ToString() }));

        md.AppendLine("## Generated output on disk").AppendLine();
        Table(md, ["Folder", "Files", "Newest"], r.Generated.Select(g => new[] { g.Path, g.Files.ToString(), g.Newest?.ToString("u") ?? "missing" }));
        var newestGenerated = r.Generated.Max(g => g.Newest);
        if (newestGenerated == null)
            md.AppendLine("No generated output. M# must run once before `eject`.").AppendLine();
        else if (r.NewestDslWrite > newestGenerated)
            md.AppendLine("Generated output is older than the M# definitions. Rebuild before `eject`.").AppendLine();

        md.AppendLine("## Windows-only or Geeks build steps").AppendLine();
        Findings(md, r.BuildSteps);

        md.AppendLine("## Deployment blockers").AppendLine();
        foreach (var group in r.Blockers.GroupBy(b => b.Rule).OrderBy(g => g.Key))
        {
            md.AppendLine($"### {group.Key} ({group.Count()})").AppendLine();
            Findings(md, group);
        }

        md.AppendLine("## Olive symbol usage").AppendLine();
        if (!r.Semantic) return md.AppendLine("Skipped (`--no-semantic`).").ToString();
        if (r.LoadErrors.Count > 0)
            md.AppendLine($"{r.LoadErrors.Count} project load errors, counts may be low:").AppendLine()
              .AppendJoin("\n", r.LoadErrors.Distinct().Take(20).Select(e => $"- {e}")).AppendLine().AppendLine();

        Table(md, ["Assembly", "Distinct symbols", "Uses", "In generated code"],
            r.Symbols.GroupBy(s => s.Assembly).OrderByDescending(g => g.Sum(s => s.Count))
                .Select(g => new[] { g.Key, g.Count().ToString(), g.Sum(s => s.Count).ToString(), g.Sum(s => s.GeneratedCount).ToString() }));
        Table(md, ["Symbol", "Kind", "Assembly", "Uses", "Generated", "Files"],
            r.Symbols.Select(s => new[] { $"`{s.Symbol}`", s.Kind, s.Assembly, s.Count.ToString(), s.GeneratedCount.ToString(), s.Files.ToString() }));

        return md.ToString();
    }

    static void Findings(StringBuilder md, IEnumerable<Finding> findings) =>
        Table(md, ["File", "Line", "Text"], findings.Select(f => new[] { f.File, f.Line.ToString(), $"`{f.Text.Replace("`", "'")}`" }));

    static void Table(StringBuilder md, string[] headers, IEnumerable<string[]> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0) { md.AppendLine("None.").AppendLine(); return; }
        md.AppendLine("| " + string.Join(" | ", headers) + " |");
        md.AppendLine("|" + string.Concat(headers.Select(_ => "---|")));
        foreach (var row in list) md.AppendLine("| " + string.Join(" | ", row.Select(c => c.Replace("|", "\\|"))) + " |");
        md.AppendLine();
    }
}
