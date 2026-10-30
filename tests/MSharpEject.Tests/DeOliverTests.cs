using MSharpEject.DeOlive;
using MSharpEject.Eject;

namespace MSharpEject.Tests;

public class DeOliverTests
{
    static async Task<TempDir> EjectedRepoAsync(bool withTests = false)
    {
        var repo = FakeRepo.Create(withTests);
        Directory.Delete(Path.Combine(repo.Path, "M#"), recursive: true);
        repo.Write("Sample.slnx", EjectTransforms.Slnx(File.ReadAllText(Path.Combine(repo.Path, "Sample.slnx")))!);
        await FakeRepo.CommitAllAsync(repo.Path);
        return repo;
    }

    [Test]
    public async Task Refuses_while_msharp_is_still_there()
    {
        using var repo = FakeRepo.Create();

        var outcome = await DeOliver.RunAsync(repo.Path, FakeRepo.NoVerify(dryRun: true), TestLog.Create(), CancellationToken.None);

        await Assert.That(outcome.Verdict).Contains("Run eject first");
    }

    [Test]
    [Timeout(300_000)]
    public async Task Dry_run_lists_rewrites_without_touching_files(CancellationToken _)
    {
        using var repo = await EjectedRepoAsync();
        var before = File.ReadAllText(Path.Combine(repo.Path, "Domain", "Logic", "Contact.cs"));

        var outcome = await DeOliver.RunAsync(repo.Path, FakeRepo.NoVerify(dryRun: true), TestLog.Create(), CancellationToken.None);

        await Assert.That(outcome.Changes.Single().Path).IsEqualTo("Domain/Logic/Contact.cs");
        await Assert.That(outcome.Changes.Single().Reason).IsEqualTo("Olive → .NET: HasValue ×2, None ×1");
        await Assert.That(File.ReadAllText(Path.Combine(repo.Path, "Domain", "Logic", "Contact.cs"))).IsEqualTo(before);
    }

    [Test]
    [Timeout(600_000)]
    public async Task Apply_rewrites_builds_and_keeps_tests_green(CancellationToken _)
    {
        using var repo = await EjectedRepoAsync(withTests: true);

        var outcome = await DeOliver.RunAsync(repo.Path, new RunOptions(), TestLog.Create(), CancellationToken.None);

        await Assert.That(outcome.Verdict).IsNull();
        await Assert.That(outcome.After!.Passed).IsEqualTo(2);
        var code = File.ReadAllText(Path.Combine(repo.Path, "Domain", "Logic", "Contact.cs"));
        await Assert.That(code).Contains("public bool Named => !string.IsNullOrEmpty(Name);");
        await Assert.That(code).Contains("public bool Unnamed => string.IsNullOrEmpty(Name);");
        await Assert.That(code).Contains("all.Any(c => c.Named)");
        await Assert.That(code).DoesNotContain("using Olive;");
        await Assert.That(code).Contains("using System.Linq;");
        var crlf = code.Contains("\r\n");
        await Assert.That(crlf ? code.Replace("\r\n", "").Contains('\n') : code.Contains('\r')).IsFalse();
    }
}
