namespace MSharpEject.Tests;

public class ProcessRunnerTests
{
    [Test]
    public async Task Captures_exit_code_and_output_into_the_log()
    {
        using var log = TestLog.Create();

        var result = await ProcessRunner.RunAsync("dotnet", "--version", Environment.CurrentDirectory, log);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output.Trim()).IsNotEmpty();
        var text = RunLogTests.ReadShared(log.FilePath);
        await Assert.That(text).Contains("> dotnet --version");
        await Assert.That(text).Contains("Exit code 0");
    }

    [Test]
    public async Task Non_zero_exit_is_returned_not_thrown()
    {
        using var log = TestLog.Create();

        var result = await ProcessRunner.RunAsync("dotnet", "no-such-command-xyz", Environment.CurrentDirectory, log);

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
    }

    [Test]
    public async Task Children_run_hidden_without_node_reuse_or_prompts()
    {
        var start = ProcessRunner.CreateStartInfo("dotnet", "build", ".", new Dictionary<string, string> { ["MSHARP_BUILD"] = "FULL" });

        await Assert.That(start.CreateNoWindow).IsTrue();
        await Assert.That(start.UseShellExecute).IsFalse();
        await Assert.That(start.RedirectStandardInput && start.RedirectStandardOutput && start.RedirectStandardError).IsTrue();
        await Assert.That(start.Environment["MSBUILDDISABLENODEREUSE"]).IsEqualTo("1");
        await Assert.That(start.Environment["GIT_TERMINAL_PROMPT"]).IsEqualTo("0");
        await Assert.That(start.Environment["MSHARP_BUILD"]).IsEqualTo("FULL");
    }

    [Test]
    [Timeout(30_000)]
    public async Task A_child_waiting_on_input_gets_end_of_file_instead_of_hanging(CancellationToken _)
    {
        using var log = TestLog.Create();

        // sort reads stdin until EOF on Windows, Linux and macOS.
        var result = await ProcessRunner.RunAsync("sort", "", Environment.CurrentDirectory, log);

        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Cancel_kills_the_process(CancellationToken _)
    {
        using var log = TestLog.Create();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var (exe, args) = OperatingSystem.IsWindows() ? ("ping", "-n 60 127.0.0.1") : ("sleep", "60");

        await Assert.That(async () => await ProcessRunner.RunAsync(exe, args, Environment.CurrentDirectory, log, cancel.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(RunLogTests.ReadShared(log.FilePath)).Contains($"Killed {exe} on cancel");
    }
}
