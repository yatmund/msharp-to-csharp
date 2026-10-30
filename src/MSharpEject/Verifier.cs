using System.Xml.Linq;

namespace MSharpEject;

public record VerifyResult(bool Built, bool HasTests, int? TestExitCode, IReadOnlySet<string> FailedTests, int Passed);

public static class Verifier
{
    // Stops M#'s AfterBuild code generation while the DSL projects still exist (baseline run).
    static readonly Dictionary<string, string> Environment = new() { ["MSHARP_BUILD"] = "FULL" };

    public static string? FindSolution(string repo) =>
        Directory.GetFiles(repo, "*.slnx").Concat(Directory.GetFiles(repo, "*.sln")).OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();

    public static bool IsTestProject(string csprojXml) =>
        new[] { "Microsoft.NET.Test.Sdk", "TUnit", "<IsTestProject>true", "NUnit", "xunit", "MSTest" }
            .Any(marker => csprojXml.Contains(marker, StringComparison.OrdinalIgnoreCase));

    public static async Task<VerifyResult> RunAsync(string repo, string label, RunLog log, CancellationToken cancel)
    {
        var solution = FindSolution(repo) ?? throw new InvalidOperationException("No .sln or .slnx in the repository root to build.");
        var name = Path.GetFileName(solution);

        log.Info($"{label}: building {name}");
        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{solution}\" -nologo -v:m", repo, log, cancel, Environment);
        if (build.ExitCode != 0)
        {
            log.Warn($"{label}: build failed (exit {build.ExitCode}). The errors are in the log.");
            return new VerifyResult(false, false, null, new HashSet<string>(), 0);
        }

        var hasTests = Audit.RepoScan.RepoFiles(repo)
            .Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Any(f => IsTestProject(File.ReadAllText(f)));
        if (!hasTests)
        {
            log.Info($"{label}: built; the repository has no test projects");
            return new VerifyResult(true, false, null, new HashSet<string>(), 0);
        }

        var results = Directory.CreateTempSubdirectory("msharp-eject-trx-").FullName;
        log.Info($"{label}: running tests");
        var test = await ProcessRunner.RunAsync("dotnet",
            $"test \"{solution}\" --no-build --logger \"trx\" --results-directory \"{results}\"", repo, log, cancel, Environment);

        var (failed, passed) = ReadTrx(Directory.GetFiles(results, "*.trx", SearchOption.AllDirectories));
        log.Info($"{label}: tests exit {test.ExitCode}, {passed} passed, {failed.Count} failed");
        foreach (var t in failed.Order()) log.Detail($"  failed: {t}");
        return new VerifyResult(true, true, test.ExitCode, failed, passed);
    }

    public static (HashSet<string> Failed, int Passed) ReadTrx(IEnumerable<string> trxFiles)
    {
        var failed = new HashSet<string>();
        var passed = 0;
        foreach (var file in trxFiles)
            foreach (var result in XDocument.Load(file).Descendants().Where(e => e.Name.LocalName == "UnitTestResult"))
            {
                var outcome = (string?)result.Attribute("outcome");
                if (outcome == "Passed") passed++;
                else if (outcome is "Failed" or "Error" or "Timeout" or "Aborted") failed.Add((string?)result.Attribute("testName") ?? "?");
            }
        return (failed, passed);
    }

    /// <returns>Null when the change is safe, otherwise why it isn't.</returns>
    public static string? Compare(VerifyResult baseline, VerifyResult after)
    {
        if (!after.Built) return "The repository doesn't build after the change.";
        if (!after.HasTests) return null;

        var newFailures = after.FailedTests.Except(baseline.FailedTests).Order().ToList();
        if (newFailures.Count > 0) return $"{newFailures.Count} tests fail now that passed before: {string.Join(", ", newFailures.Take(10))}";
        if (after.TestExitCode != 0 && after.FailedTests.Count == 0 && baseline.TestExitCode == 0)
            return "Tests exited with an error but no failures were reported. See the log.";
        return null;
    }
}
