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
