using System.Diagnostics;
using MSharpEject.Audit;

namespace MSharpEject.Tests;

public class ClaudeHandoffTests
{
    static readonly AuditResult Result = new("/work/Shop", [new("Domain/Domain.csproj", "Olive.Entities", "2")], [], [], [], null,
        [new("Logic/Pdf.cs", 3, "font", "new FontFamily(\"Calibri\")")], true, [new("Olive.Entities", "IDatabase.GetList", "method", 12, 4, 3)], []);

    [Test]
    public async Task Prompt_sets_the_goal_and_carries_what_the_audit_found()
    {
        var prompt = ClaudeHandoff.Prompt(Result, "/tools/MSharpEject");

        await Assert.That(prompt).Contains("/work/Shop");
        await Assert.That(prompt).Contains("docs and tests");
        await Assert.That(prompt).Contains("Never copy its source");
        await Assert.That(prompt).Contains("- Olive-free: no. 1 Geeks package references and 12 Olive symbol uses remain.");
        await Assert.That(prompt).Contains("- Olive · Data layer: Olive.Entities;");
        await Assert.That(prompt).Contains("- Linux · font: 1 in 1 files.");
        await Assert.That(prompt).Contains("IDatabase.GetList (12)");
        await Assert.That(prompt).Contains("`\"/tools/MSharpEject\" audit \"/work/Shop\"`");
    }

    [Test]
    public async Task Prompt_without_cli_skips_the_audit_loop() =>
        await Assert.That(ClaudeHandoff.Prompt(Result, null)).DoesNotContain("To check your work");

    [Test]
    public async Task Terminal_script_quotes_paths_with_spaces_and_apostrophes()
    {
        using var dir = new TempDir();
        var promptFile = Path.Combine(dir.Path, "claude-prompt.md");
        var repo = Path.Combine(dir.Path, "Bob's repo");

        ProcessStartInfo? start = null;
        try { start = ClaudeHandoff.Terminal(repo, promptFile); }
        catch (InvalidOperationException) when (OperatingSystem.IsLinux()) { }

        var script = Directory.GetFiles(dir.Path, "claude-handoff.*").Single();
        var text = File.ReadAllText(script);
        if (OperatingSystem.IsWindows())
        {
            await Assert.That(text).Contains($"Set-Location -LiteralPath '{repo.Replace("'", "''")}'");
            await Assert.That(start!.Arguments).Contains(script);
        }
        else
            await Assert.That(text).Contains("'\\''");
        await Assert.That(text).Contains("claude");
    }
}
