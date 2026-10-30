using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace MSharpEject;

public sealed class LoadedProjects(MSBuildWorkspace workspace, List<Project> projects, List<string> errors) : IDisposable
{
    public MSBuildWorkspace Workspace { get; } = workspace;
    public List<Project> Projects { get; } = projects;
    public List<string> Errors { get; } = errors;

    public void Dispose() => Workspace.Dispose();
}

public static class ProjectLoader
{
    /// <summary>Restores then loads the projects with their compilations. Progress runs from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static async Task<LoadedProjects> LoadAsync(string repo, IReadOnlyList<string> projects, RunLog log, double from, double to, CancellationToken cancel)
    {
        string Rel(string f) => Path.GetRelativePath(repo, f).Replace('\\', '/');
        var errors = new List<string>();
        var wanted = projects.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var solution = Verifier.FindSolution(repo);
        var step = (to - from) / 4;

        log.Progress(from, "Checking .NET SDK");
        await ProcessRunner.RunAsync("dotnet", "--info", repo, log, cancel);

        // One solution restore lets NuGet work out the whole graph in parallel; per-project restores repeat it.
        var restoreTargets = solution != null ? [solution] : projects.ToList();
        for (var i = 0; i < restoreTargets.Count; i++)
        {
            log.Progress(from + step * (1 + (double)i / restoreTargets.Count), $"Restoring {Rel(restoreTargets[i])}" + (restoreTargets.Count > 1 ? $" ({i + 1}/{restoreTargets.Count})" : ""));
            using (log.Time("restore " + Rel(restoreTargets[i])))
            {
                var restore = await ProcessRunner.RunAsync("dotnet", $"restore \"{restoreTargets[i]}\"", repo, log, cancel);
                if (restore.ExitCode != 0)
                {
                    log.Warn($"Restore failed for {Rel(restoreTargets[i])} (exit {restore.ExitCode}). Results may be incomplete. Full output is in the log.");
                    errors.Add($"Restore failed: {Rel(restoreTargets[i])}");
                }
            }
        }

        // Roslyn's build host inherits this; ProcessRunner sets it for everything else.
        Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
        var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            log.Detail($"Workspace {e.Diagnostic.Kind}: {e.Diagnostic.Message}");
            // The M# DSL projects get loaded with the solution but aren't analysed; their failures are noise.
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure && !e.Diagnostic.Message.Contains("M#"))
                lock (errors) errors.Add(e.Diagnostic.Message);
        });

        log.Progress(from + step * 2, solution != null ? $"Loading {Rel(solution)}" : $"Loading {projects.Count} projects");
        var loaded = new List<Project>();
        using (log.Time("load projects"))
        {
            if (solution != null)
                loaded.AddRange((await workspace.OpenSolutionAsync(solution, cancellationToken: cancel)).Projects
                    .Where(p => p.FilePath != null && wanted.Contains(p.FilePath)));
            foreach (var path in wanted.Where(w => loaded.All(p => !string.Equals(p.FilePath, w, StringComparison.OrdinalIgnoreCase))))
            {
                log.Progress(from + step * 2, $"Loading {Rel(path)}");
                loaded.Add(workspace.CurrentSolution.Projects.FirstOrDefault(p => string.Equals(p.FilePath, path, StringComparison.OrdinalIgnoreCase))
                    ?? await workspace.OpenProjectAsync(path, cancellationToken: cancel));
            }
        }

        // Projects compile concurrently; Roslyn caches the results for later GetCompilationAsync calls.
        log.Progress(from + step * 3, $"Compiling {loaded.Count} projects");
        using (log.Time("compile projects"))
        {
            var compilations = await Task.WhenAll(loaded.Select(p => p.GetCompilationAsync(cancel)));
            for (var i = 0; i < loaded.Count; i++)
            {
                if (compilations[i] is not { } compilation) continue;
                // Expected while generated code is missing; logged so low counts can be explained.
                var compileErrors = compilation.GetDiagnostics(cancel).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                log.Detail($"{Rel(loaded[i].FilePath!)}: {compilation.SyntaxTrees.Count()} files, {compilation.References.Count()} references, {compileErrors.Count} compile errors");
                foreach (var d in compileErrors.Take(100)) log.Detail("  " + d);
            }
        }

        return new LoadedProjects(workspace, loaded.OrderBy(p => p.FilePath, StringComparer.Ordinal).ToList(), errors);
    }
}
