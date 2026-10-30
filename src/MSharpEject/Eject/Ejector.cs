using MSharpEject.Audit;

namespace MSharpEject.Eject;

public record EjectPlan(List<Change> Changes, List<string> Problems, List<string> Warnings);

public record RunOptions(bool DryRun = false, bool AllowDirty = false, bool Verify = true, bool Generate = false);

/// <summary>Outcome of eject or de-olive. <see cref="Verdict"/> null means verified safe (or not verified).</summary>
public record RunOutcome(List<Change> Changes, bool Applied, VerifyResult? Baseline, VerifyResult? After, string? Verdict, List<string> Notes);

public static class Ejector
{
    public static EjectPlan Plan(string repo)
    {
        repo = Path.GetFullPath(repo);
        string Rel(string f) => Path.GetRelativePath(repo, f).Replace('\\', '/');
        var changes = new List<Change>();
        var problems = new List<string>();
        var warnings = new List<string>();

        var mSharpFolders = new[] { "M#", "@M#" }.Where(d => Directory.Exists(Path.Combine(repo, d))).ToList();
        if (mSharpFolders.Count == 0) problems.Add("No M# folder: there's nothing to eject.");

        var generated = RepoScan.GeneratedFolderPaths.Select(g => RepoScan.ScanGeneratedFolder(repo, g)).ToList();
        var hasEntities = generated.Any(g => g.Path.StartsWith("Domain/[GEN-") && g.Files > 0);
        var hasUi = generated.Any(g => g.Path.StartsWith("Website/") && g.Files > 0);
        if (mSharpFolders.Count > 0 && !hasEntities)
            problems.Add("M#'s generated entities (Domain/[GEN-Entities]) aren't on disk. Build once the M# way, or run eject with --generate on Windows.");
        if (mSharpFolders.Count > 0 && hasEntities && !hasUi && Directory.Exists(Path.Combine(repo, "M#", "UI")))
            problems.Add("M#'s generated controllers and views (Website/Controllers/Modules, Website/Views/Modules) aren't on disk. Build the UI the M# way first, or use --generate.");

        var dslFiles = mSharpFolders.SelectMany(d => Directory.GetFiles(Path.Combine(repo, d), "*.cs", SearchOption.AllDirectories)).ToList();
        var newestGenerated = generated.Max(g => g.Newest);
        if (newestGenerated != null && dslFiles.Count > 0 && dslFiles.Max(File.GetLastWriteTimeUtc) > newestGenerated)
            warnings.Add("Some M# definitions are newer than the generated code. If you changed them since the last build, rebuild the M# way first or those changes are lost. (A fresh clone can trigger this falsely.)");

        var gitignore = Path.Combine(repo, ".gitignore");
        if (File.Exists(gitignore) && Edit(gitignore, EjectTransforms.Gitignore) is { } ignoreEdit)
            changes.Add(new EditFile(".gitignore", "Stop ignoring M#'s generated code so it's committed as source", ignoreEdit.Text, ignoreEdit.Encoding));

        foreach (var solution in Directory.GetFiles(repo, "*.sln").Concat(Directory.GetFiles(repo, "*.slnx")))
            if (Edit(solution, solution.EndsWith(".slnx") ? EjectTransforms.Slnx : EjectTransforms.Sln) is { } slnEdit)
                changes.Add(new EditFile(Rel(solution), "Remove the M# #Model and #UI projects", slnEdit.Text, slnEdit.Encoding));

        var files = RepoScan.RepoFiles(repo).ToList();
        foreach (var csproj in files.Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && RepoScan.IsCodeProject(Rel(f))))
            PlanProject(repo, csproj, changes);

        foreach (var dockerfile in files.Where(f => Path.GetFileName(f).StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase)))
        {
            var project = Directory.GetFiles(Path.GetDirectoryName(dockerfile)!, "*.csproj").FirstOrDefault();
            if (project == null || EjectTransforms.DotnetVersion(File.ReadAllText(project)) is not { } version) continue;
            if (Edit(dockerfile, text => EjectTransforms.Dockerfile(text, version)) is { } dockerEdit)
                changes.Add(new EditFile(Rel(dockerfile), $"Use current Linux .NET {version} images in place of retired or Windows-only ones", dockerEdit.Text, dockerEdit.Encoding));
        }

        foreach (var manifest in files.Where(f => Path.GetFileName(f) == "dotnet-tools.json"))
            if (Edit(manifest, EjectTransforms.ToolsManifest) is { } toolsEdit)
                changes.Add(new EditFile(Rel(manifest), "Remove Geeks dotnet tools (msharp-build)", toolsEdit.Text, toolsEdit.Encoding));

        foreach (var script in files.Where(f => Path.GetExtension(f).ToLowerInvariant() is ".bat" or ".cmd" && Path.GetDirectoryName(f) == repo))
            if (File.ReadAllText(script).Contains("msharp", StringComparison.OrdinalIgnoreCase))
                changes.Add(new DeleteFile(Rel(script), "Windows build script for M#; the build is now plain `dotnet build`"));

        foreach (var webConfig in files.Where(f => Rel(f).EndsWith("/Views/Web.config", StringComparison.OrdinalIgnoreCase)))
            changes.Add(new DeleteFile(Rel(webConfig), "Legacy MVC 5 Razor config; ASP.NET Core ignores it, and it references MSharp.Framework"));

        var dbWarning = Path.Combine(repo, "DB", "Tables", "-Warning!.txt");
        if (File.Exists(dbWarning)) changes.Add(new DeleteFile("DB/Tables/-Warning!.txt", "Says the folder is regenerated by M#, which is no longer true"));

        foreach (var temp in Directory.GetDirectories(repo, "_M#_TEMP", SearchOption.AllDirectories)
                     .Where(d => RepoScan.IsCodeProject(Rel(d)) && !Rel(d).Split('/').Any(p => p is "bin" or "obj" or "node_modules")))
            changes.Add(new DeleteDirectory(Rel(temp), "M# IntelliSense scratch folder"));
        foreach (var folder in mSharpFolders)
            changes.Add(new DeleteDirectory(folder, "The M# definitions; their output is now committed source"));

        return new EjectPlan(changes, problems, warnings);
    }

    static void PlanProject(string repo, string csproj, List<Change> changes)
    {
        string Rel(string f) => Path.GetRelativePath(repo, f).Replace('\\', '/');
        var dir = Path.GetDirectoryName(csproj)!;
        var (xml, encoding) = FileText.Read(csproj);

        var compilerConfig = Path.Combine(dir, "compilerconfig.json");
        var usesWebCompiler = xml.Contains("WebCompiler.", StringComparison.Ordinal);
        var sass = usesWebCompiler && File.Exists(compilerConfig) ? EjectTransforms.SassPairs(File.ReadAllText(compilerConfig)) : [];
        var packageJson = File.Exists(Path.Combine(dir, "package.json"));
        var frontEnd = packageJson || usesWebCompiler
            ? new FrontEnd(packageJson, File.Exists(Path.Combine(dir, ".yarnrc")) || File.Exists(Path.Combine(dir, "yarn.lock")),
                File.Exists(Path.Combine(dir, "tsconfig.json")), sass)
            : null;

        if (EjectTransforms.Csproj(xml, frontEnd) is not { } newXml) return;
        changes.Add(new EditFile(Rel(csproj), Describe(xml, frontEnd), newXml, encoding));

        if (!usesWebCompiler) return;
        foreach (var config in new[] { "compilerconfig.json", "compilerconfig.json.defaults" }.Select(n => Path.Combine(dir, n)).Where(File.Exists))
            changes.Add(new DeleteFile(Rel(config), "WebCompiler settings; Sass is compiled by the CompileSass target now"));

        foreach (var task in System.Text.RegularExpressions.Regex.Matches(xml, @"AssemblyFile=""([^""]*SassCompiler\.exe)""").Select(m => m.Groups[1].Value).Distinct())
        {
            var toolDir = Path.GetDirectoryName(Path.Combine(dir, task.Replace('\\', Path.DirectorySeparatorChar)))!;
            var actual = Directory.Exists(toolDir) ? toolDir : FindCaseInsensitive(dir, task);
            if (actual != null) changes.Add(new DeleteDirectory(Rel(actual), "Windows-only SassCompiler.exe and its batch file"));
        }
    }

    // Template says wwwroot\Styles\build but the folder is wwwroot/styles/build; matters on Linux.
    static string? FindCaseInsensitive(string root, string relativeFile)
    {
        var current = root;
        foreach (var part in relativeFile.Replace('\\', '/').Split('/').SkipLast(1))
        {
            current = Directory.Exists(current) ? Directory.GetDirectories(current).FirstOrDefault(d => Path.GetFileName(d).Equals(part, StringComparison.OrdinalIgnoreCase)) : null;
            if (current == null) return null;
        }
        return current;
    }

    static string Describe(string xml, FrontEnd? frontEnd)
    {
        var parts = new List<string>();
        if (xml.Contains("\"MSharp", StringComparison.OrdinalIgnoreCase)) parts.Add("remove MSharp package");
        if (xml.Contains("WebCompiler.")) parts.Add("replace Windows-only WebCompiler");
        if (frontEnd?.HasPackageJson == true) parts.Add("install front-end packages on build");
        if (frontEnd?.Sass.Count > 0) parts.Add("compile Sass with dart-sass");
        if (frontEnd?.HasTsConfig == true) parts.Add("compile TypeScript with Microsoft.TypeScript.MSBuild");
        var text = string.Join(", ", parts);
        return text.Length == 0 ? "Tidy project file" : char.ToUpperInvariant(text[0]) + text[1..];
    }

    static (string Text, System.Text.Encoding Encoding)? Edit(string path, Func<string, string?> transform)
    {
        var (text, encoding) = FileText.Read(path);
        return transform(text) is { } changed ? (changed, encoding) : null;
    }

    public static async Task<RunOutcome> RunAsync(string repo, RunOptions options, RunLog log, CancellationToken cancel)
    {
        repo = Path.GetFullPath(repo);
        log.Info($"Ejecting M# from {repo}{(options.DryRun ? " (dry run)" : "")}");
        var notes = new List<string>();

        if (!options.DryRun && !options.AllowDirty)
        {
            log.Progress(0, "Checking git status");
            if (await Git.WhyNotCleanAsync(repo, log, cancel) is { } dirty) return Stop(dirty);
        }

        if (options.Generate)
        {
            if (!OperatingSystem.IsWindows()) return Stop("--generate runs M# itself, which only works on Windows. Generate on a Windows machine or CI runner, commit nothing, then eject anywhere.");
            await GenerateAsync(repo, log, cancel);
        }

        log.Progress(0.05, "Planning changes");
        var plan = Plan(repo);
        foreach (var change in plan.Changes) log.Info($"  {change.Action}: {change.Path} ({change.Reason})");
        foreach (var warning in plan.Warnings) log.Warn(warning);
        notes.AddRange(plan.Warnings);
        if (plan.Problems.Count > 0)
        {
            foreach (var problem in plan.Problems) log.Error(problem);
            return new RunOutcome(plan.Changes, false, null, null, string.Join(" ", plan.Problems), notes);
        }
        if (options.DryRun)
        {
            log.Progress(1, $"Dry run: {plan.Changes.Count} changes planned");
            return new RunOutcome(plan.Changes, false, null, null, null, notes);
        }

        return await ApplyAndVerifyAsync(repo, plan.Changes, () => ChangeApplier.Apply(repo, plan.Changes, log), options, log, notes, cancel);

        RunOutcome Stop(string reason)
        {
            log.Error(reason);
            return new RunOutcome([], false, null, null, reason, notes);
        }
    }

    /// <summary>Shared by eject and de-olive: baseline build/test, apply, build/test again, compare.</summary>
    public static async Task<RunOutcome> ApplyAndVerifyAsync(string repo, List<Change> changes, Action apply, RunOptions options, RunLog log, List<string> notes, CancellationToken cancel)
    {
        VerifyResult? baseline = null;
        if (options.Verify)
        {
            log.Progress(0.1, "Baseline: building and testing before any change");
            baseline = await Verifier.RunAsync(repo, "Baseline", log, cancel);
            if (!baseline.Built) notes.Add("The repository didn't build before the change either, so the result below can't be blamed on this tool alone.");
        }

        log.Progress(0.5, $"Applying {changes.Count} changes");
        cancel.ThrowIfCancellationRequested();
        apply();

        if (!options.Verify)
        {
            log.Progress(1, "Done (not verified)");
            return new RunOutcome(changes, true, null, null, null, notes);
        }

        log.Progress(0.6, "Verifying: building and testing after the change");
        var after = await Verifier.RunAsync(repo, "After", log, cancel);
        var verdict = Verifier.Compare(baseline!, after);
        if (verdict == null) log.Info("Verified: builds" + (after.HasTests ? $", {after.Passed} tests pass, no new failures" : ""));
        else log.Error(verdict + " Review with `git diff`; undo everything with `git checkout -- .`");
        log.Progress(1, verdict == null ? "Done" : "Done, with problems");
        return new RunOutcome(changes, true, baseline, after, verdict, notes);
    }

    static async Task GenerateAsync(string repo, RunLog log, CancellationToken cancel)
    {
        string[] steps = ["M#/Model/#Model.csproj", "Domain/Domain.csproj", "M#/UI/#UI.csproj"];
        for (var i = 0; i < steps.Length; i++)
        {
            var project = Path.Combine(repo, steps[i]);
            if (!File.Exists(project)) continue;
            log.Progress(0.01 + 0.04 * i / steps.Length, $"Running M# generation: building {steps[i]}");
            var build = await ProcessRunner.RunAsync("dotnet", $"build \"{project}\" -nologo -v:m", repo, log, cancel);
            if (build.ExitCode != 0) throw new InvalidOperationException($"M# generation failed building {steps[i]} (exit {build.ExitCode}). See the log.");
        }
    }
}
