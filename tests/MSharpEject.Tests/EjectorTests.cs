using MSharpEject.Eject;

namespace MSharpEject.Tests;

public class EjectorTests
{
    static Dictionary<string, string> Snapshot(string repo) =>
        Directory.GetFiles(repo, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
            .ToDictionary(f => Path.GetRelativePath(repo, f), File.ReadAllText);

    [Test]
    public async Task Plan_lists_every_change_for_a_typical_repo()
    {
        using var repo = FakeRepo.Create();

        var plan = Ejector.Plan(repo.Path);

        await Assert.That(plan.Problems).IsEmpty();
        await Assert.That(plan.Changes.Select(c => $"{c.Action}: {c.Path}")).IsEquivalentTo(
        [
            "Edit: .gitignore",
            "Edit: Sample.slnx",
            "Delete file: Build.bat",
            "Delete folder: M#",
        ]);
    }

    [Test]
    public async Task Plan_refuses_without_msharp_or_generated_code()
    {
        using var none = new TempDir();
        none.Write("Domain/Domain.csproj", "<Project />");
        using var ungenerated = FakeRepo.Create(generated: false);

        await Assert.That(Ejector.Plan(none.Path).Problems.Single()).Contains("nothing to eject");
        await Assert.That(Ejector.Plan(ungenerated.Path).Problems.Single()).Contains("--generate");
    }

    [Test]
    public async Task Dry_run_changes_nothing()
    {
        using var repo = FakeRepo.Create();
        var before = Snapshot(repo.Path);

        var outcome = await Ejector.RunAsync(repo.Path, FakeRepo.NoVerify(dryRun: true), TestLog.Create(), CancellationToken.None);

        await Assert.That(outcome.Changes.Count).IsEqualTo(4);
        await Assert.That(outcome.Applied).IsFalse();
        await Assert.That(Snapshot(repo.Path)).IsEquivalentTo(before);
    }

    [Test]
    public async Task Refuses_outside_git_and_with_uncommitted_changes()
    {
        using var noGit = FakeRepo.Create();
        using var dirty = FakeRepo.Create();
        await FakeRepo.CommitAllAsync(dirty.Path);
        dirty.Write("Domain/Logic/New.cs", "// uncommitted");

        var outside = await Ejector.RunAsync(noGit.Path, FakeRepo.NoVerify(), TestLog.Create(), CancellationToken.None);
        var uncommitted = await Ejector.RunAsync(dirty.Path, FakeRepo.NoVerify(), TestLog.Create(), CancellationToken.None);

        await Assert.That(outside.Verdict).Contains("Not a git repository");
        await Assert.That(uncommitted.Verdict).Contains("uncommitted changes");
        await Assert.That(Directory.Exists(Path.Combine(dirty.Path, "M#"))).IsTrue();
    }

    [Test]
    public async Task Apply_removes_msharp_and_a_second_run_has_nothing_to_do()
    {
        using var repo = FakeRepo.Create();
        await FakeRepo.CommitAllAsync(repo.Path);

        var outcome = await Ejector.RunAsync(repo.Path, FakeRepo.NoVerify(), TestLog.Create(), CancellationToken.None);

        await Assert.That(outcome.Verdict).IsNull();
        await Assert.That(outcome.Applied).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(repo.Path, "M#"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(repo.Path, "Build.bat"))).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(repo.Path, ".gitignore"))).IsEqualTo("bin/\nobj/\n");
        await Assert.That(File.ReadAllText(Path.Combine(repo.Path, "Sample.slnx"))).DoesNotContain("M#");
        await Assert.That(Ejector.Plan(repo.Path).Problems.Single()).Contains("nothing to eject");
    }

    [Test]
    [Timeout(600_000)]
    public async Task Apply_with_verification_builds_and_tests_before_and_after(CancellationToken _)
    {
        using var repo = FakeRepo.Create(withTests: true);
        await FakeRepo.CommitAllAsync(repo.Path);
        using var log = TestLog.Create();

        var outcome = await Ejector.RunAsync(repo.Path, new RunOptions(), log, CancellationToken.None);

        await Assert.That(outcome.Verdict).IsNull();
        await Assert.That(outcome.Baseline!.Built).IsTrue();
        await Assert.That(outcome.Baseline.Passed).IsEqualTo(2);
        await Assert.That(outcome.After!.Built).IsTrue();
        await Assert.That(outcome.After.Passed).IsEqualTo(2);
        await Assert.That(outcome.After.FailedTests).IsEmpty();
        await Assert.That(RunLogTests.ReadShared(log.FilePath)).Contains("Verified: builds, 2 tests pass");
    }

    [Test]
    public async Task Report_text_describes_changes_and_verdict()
    {
        var outcome = new RunOutcome([new DeleteFile("Build.bat", "Windows build script")], true,
            new VerifyResult(true, true, 0, new HashSet<string>(), 5), new VerifyResult(true, true, 1, new HashSet<string> { "T1" }, 4),
            "1 tests fail now that passed before: T1", []);

        var text = RunReport.ToText("eject", outcome, new RunOptions());

        await Assert.That(text).Contains("eject: made 1 changes");
        await Assert.That(text).Contains("Build.bat  (Windows build script)");
        await Assert.That(text).Contains("before builds, 5 passed, 0 failed; after builds, 4 passed, 1 failed");
        await Assert.That(text).Contains("PROBLEM: 1 tests fail now that passed before: T1 Undo with `git checkout -- .`");
    }
}
