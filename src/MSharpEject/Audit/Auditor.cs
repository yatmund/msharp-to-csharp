using System.Text;
using Microsoft.CodeAnalysis;

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
    public static async Task<AuditResult> RunAsync(string repo, bool semantic, RunLog log, CancellationToken cancel = default)
    {
        repo = Path.GetFullPath(repo);
        string Rel(string f) => Path.GetRelativePath(repo, f).Replace('\\', '/');
        log.Info($"Auditing {repo} ({(semantic ? "full" : "file scans only")})");
        var scanShare = semantic ? 0.1 : 1.0;

        log.Progress(0, "Listing files");
        var files = RepoScan.RepoFiles(repo).ToList();
        var projects = files.Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).OrderBy(Rel, StringComparer.Ordinal).ToList();
        log.Detail($"{files.Count} files, {projects.Count} projects:\n" + string.Join("\n", projects.Select(Rel)));
        cancel.ThrowIfCancellationRequested();

        log.Progress(0.2 * scanShare, "Reading package references and build steps");
        var packages = projects.SelectMany(p => RepoScan.GeeksPackages(Rel(p), File.ReadAllText(p)))
            .Concat(files.Where(f => Path.GetFileName(f) == "dotnet-tools.json").SelectMany(f => RepoScan.GeeksTools(Rel(f), File.ReadAllText(f))))
            .Concat(files.Where(f => Path.GetFileName(f) == "package.json").SelectMany(f => RepoScan.GeeksNpmPackages(Rel(f), File.ReadAllText(f))))
            .ToList();
        var buildSteps = projects.SelectMany(p => RepoScan.BuildSteps(Rel(p), File.ReadAllText(p))).ToList();
        cancel.ThrowIfCancellationRequested();

        log.Progress(0.4 * scanShare, "Counting M# definitions");
        var dslFiles = files.Where(f => RepoScan.IsDslFile(Rel(f)) && f.EndsWith(".cs")).ToList();
        var dsl = RepoScan.DslInventory(dslFiles.Select(File.ReadAllText));

        log.Progress(0.6 * scanShare, "Checking generated output");
        var generated = RepoScan.GeneratedFolderPaths.Select(g => RepoScan.ScanGeneratedFolder(repo, g)).ToList();
        cancel.ThrowIfCancellationRequested();

        log.Progress(0.8 * scanShare, "Scanning for deployment blockers");
        var blockers = files
            .Where(f => !RepoScan.IsDslFile(Rel(f)) && !RepoScan.IsGeneratedFile(Rel(f)))
            .Where(f => new FileInfo(f).Length < 2_000_000)
            .AsParallel().AsOrdered().WithCancellation(cancel)
            .SelectMany(f => RepoScan.DeploymentBlockers(Rel(f), File.ReadAllText(f)))
            .ToList();

        var symbols = new List<SymbolUse>();
        var loadErrors = new List<string>();
        if (semantic)
            (symbols, loadErrors) = await CollectSymbolsAsync(repo, projects, Rel, log, cancel);

        var result = new AuditResult(repo, packages, buildSteps, dsl, generated,
            dslFiles.Count == 0 ? null : dslFiles.Max(File.GetLastWriteTimeUtc), blockers, semantic, symbols, loadErrors);
        log.Progress(1, "Done");
        log.Info($"{packages.Count} Geeks package references, {dsl.Values.Sum()} M# items, {generated.Sum(g => g.Files)} generated files, " +
                 $"{buildSteps.Count} build steps, {blockers.Count} blockers, {symbols.Count} Olive symbols, {loadErrors.Count} load errors");
        return result;
    }

    static async Task<(List<SymbolUse>, List<string>)> CollectSymbolsAsync(
        string repo, List<string> projects, Func<string, string> rel, RunLog log, CancellationToken cancel)
    {
        var targets = projects.Where(p => RepoScan.IsCodeProject(rel(p))).ToList();
        using var loaded = await ProjectLoader.LoadAsync(repo, targets, log, 0.1, 0.8, cancel);
        var compilations = new List<Compilation>();
        foreach (var project in loaded.Projects)
            if (await project.GetCompilationAsync(cancel) is { } compilation) compilations.Add(compilation);
        var errors = loaded.Errors;
        log.Progress(0.8, "Resolving Olive symbols");
        var lastBucket = 0;
        using (log.Time("resolve symbols"))
        {
            var symbols = OliveUsage.Collect(compilations, f => RepoScan.IsGeneratedFile(rel(f)), fraction =>
            {
                var bucket = (int)(fraction * 10);
                if (bucket > Volatile.Read(ref lastBucket) && Interlocked.Exchange(ref lastBucket, bucket) < bucket)
                    log.Progress(0.8 + 0.2 * fraction, $"Resolving Olive symbols ({bucket * 10}%)");
            }, cancel);
            return (symbols, errors);
        }
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
