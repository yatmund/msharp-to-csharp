using MSharpEject.Audit;
using MSharpEject.Gui;

namespace MSharpEject.Tests;

public class ResultViewTests
{
    static AuditResult Result(bool semantic = true, int generatedFiles = 3, DateTime? dslWrite = null) => new(
        Repo: "/repo",
        Packages: [new("Domain/Domain.csproj", "Olive", "2.1"), new("Website/Website.csproj", "Olive", "2.1"), new("Website/Website.csproj", "Olive.Mvc", "3.1")],
        BuildSteps: [new("Website/Website.csproj", 65, "build-task-exe", "SassCompiler.exe")],
        Dsl: new() { ["EntityType"] = 4, ["ListModule"] = 2, ["c# expressions"] = 9 },
        Generated: [new("Domain/[GEN-Entities]", generatedFiles, generatedFiles == 0 ? null : new DateTime(2026, 1, 2))],
        NewestDslWrite: dslWrite ?? new DateTime(2026, 1, 1),
        Blockers: [new("Build.bat", 1, "windows-script", "Build.bat"), new("Website/Dockerfile", 1, "windows-container", "FROM x-nanoserver")],
        Semantic: semantic,
        Symbols: [new("Olive", "OliveExtensions.HasValue", "extension", 10, 4, 3), new("Olive.Entities", "Olive.Entities.GuidEntity", "class", 5, 5, 5)],
        LoadErrors: []);

    [Test]
    public async Task Filter_matches_any_property_case_insensitively()
    {
        var blockers = Result().Blockers;

        await Assert.That(ResultView.Filter(blockers, "DOCKER").Single().Rule).IsEqualTo("windows-container");
        await Assert.That(ResultView.Filter(blockers, "windows-").Count).IsEqualTo(2);
        await Assert.That(ResultView.Filter(blockers, "nothing matches").Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Filter_blank_returns_everything(string? text) =>
        await Assert.That(ResultView.Filter(Result().Symbols, text).Count).IsEqualTo(2);

    [Test]
    public async Task Filter_matches_numbers_too() =>
        await Assert.That(ResultView.Filter(Result().BuildSteps, "65").Count).IsEqualTo(1);

    [Test]
    [Arguments(5, 5, "Packages (5)")]
    [Arguments(2, 5, "Packages (2/5)")]
    public async Task Header_shows_filtered_count(int shown, int total, string expected) =>
        await Assert.That(ResultView.Header("Packages", shown, total)).IsEqualTo(expected);

    [Test]
    public async Task DslRows_sorted_by_count()
    {
        var rows = ResultView.DslRows(Result());

        await Assert.That(rows[0]).IsEqualTo(new DslRow("c# expressions", 9));
        await Assert.That(rows.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Stats_count_and_flag_what_needs_attention()
    {
        var stats = ResultView.Stats(Result()).ToDictionary(s => s.Label);

        await Assert.That(stats["Geeks packages"]).IsEqualTo(new Stat("Geeks packages", "2", true));
        await Assert.That(stats["M# definitions"]).IsEqualTo(new Stat("M# definitions", "6", false));
        await Assert.That(stats["Generated files"]).IsEqualTo(new Stat("Generated files", "3", false));
        await Assert.That(stats["Build steps"].Attention).IsTrue();
        await Assert.That(stats["Deployment blockers"].Value).IsEqualTo("2");
        await Assert.That(stats["Olive symbols"]).IsEqualTo(new Stat("Olive symbols", "2", true, "6 hand-written uses"));
    }

    [Test]
    public async Task Stats_hint_missing_stale_skipped_and_load_errors()
    {
        await Assert.That(Generated(Result(generatedFiles: 0))).IsEqualTo(new Stat("Generated files", "0", true, "M# must run once first"));
        await Assert.That(Generated(Result(dslWrite: new DateTime(2026, 6, 1))).Hint).IsEqualTo("Older than the definitions");
        await Assert.That(Olive(Result(semantic: false))).IsEqualTo(new Stat("Olive symbols", "–", false, "Not resolved"));
        await Assert.That(Olive(Result() with { LoadErrors = ["x", "y"] }).Hint).IsEqualTo("2 load errors, see log");

        static Stat Generated(AuditResult r) => ResultView.Stats(r).Single(s => s.Label == "Generated files");
        static Stat Olive(AuditResult r) => ResultView.Stats(r).Single(s => s.Label == "Olive symbols");
    }
}
