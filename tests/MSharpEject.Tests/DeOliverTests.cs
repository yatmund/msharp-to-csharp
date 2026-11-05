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

    const string Names = """
        using Olive;

        namespace Domain
        {
            public static class Names
            {
                public static bool IsTeam(string name) => name.IsAnyOf("a", "b");
            }
        }
        """;

    [Test]
    [Timeout(600_000)]
    public async Task Helper_only_files_move_to_the_library_and_missing_types_are_reported(CancellationToken _)
    {
        var repo = FakeRepo.Create(withTests: true);
        repo.Write("Domain/Logic/Names.cs", Names);
        using var ejected = await EjectAsync(repo);

        var outcome = await DeOliver.RunAsync(ejected.Path, new RunOptions(), TestLog.Create(), CancellationToken.None);

        await Assert.That(outcome.Verdict).IsNull();
        await Assert.That(outcome.After!.Passed).IsEqualTo(2);
        await Assert.That(File.ReadAllText(Path.Combine(ejected.Path, "Domain", "Logic", "Names.cs"))).Contains("using Platform;");
        await Assert.That(File.Exists(Path.Combine(ejected.Path, "Platform", "Core", "Strings.cs"))).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(ejected.Path, "Domain", "Domain.csproj"))).Contains(@"..\Platform\Platform.csproj");
        await Assert.That(File.ReadAllText(Path.Combine(ejected.Path, "Domain", "Domain.csproj"))).Contains("Olive.csproj");
        await Assert.That(File.ReadAllText(Path.Combine(ejected.Path, "Sample.slnx"))).Contains("Platform/Platform.csproj");
        await Assert.That(string.Join(" ", outcome.Notes)).Contains("GuidEntity");
    }

    [Test]
    [Timeout(600_000)]
    public async Task Olive_is_removed_once_the_library_covers_everything(CancellationToken _)
    {
        var repo = FakeRepo.Create(withTests: true);
        repo.Write("Domain/Logic/Names.cs", Names);
        repo.Write("Domain/[GEN-Entities]/Contact.cs", "namespace Domain { public partial class Contact { public string Name { get; set; } } }");
        using var ejected = await EjectAsync(repo);

        var outcome = await DeOliver.RunAsync(ejected.Path, new RunOptions(), TestLog.Create(), CancellationToken.None);

        await Assert.That(outcome.Verdict).IsNull();
        await Assert.That(outcome.After!.Passed).IsEqualTo(2);
        await Assert.That(string.Join(" ", outcome.Notes)).Contains("Olive is no longer referenced");
        await Assert.That(Directory.Exists(Path.Combine(ejected.Path, "Olive"))).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(ejected.Path, "Domain", "Domain.csproj"))).DoesNotContain("Olive");
        await Assert.That(File.ReadAllText(Path.Combine(ejected.Path, "Sample.slnx"))).DoesNotContain("Olive");
    }

    static async Task<TempDir> EjectAsync(TempDir repo)
    {
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
