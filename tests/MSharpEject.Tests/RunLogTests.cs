namespace MSharpEject.Tests;

public class RunLogTests
{
    public static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new StreamReader(stream).ReadToEnd();
    }

    [Test]
    public async Task Writes_environment_header()
    {
        using var log = TestLog.Create();

        var text = ReadShared(log.FilePath);

        await Assert.That(text).Contains("msharp-eject ");
        await Assert.That(text).Contains("OS ");
        await Assert.That(text).Contains(".NET");
        await Assert.That(text).Contains("Command line:");
    }

    [Test]
    public async Task Info_warn_error_echo_but_detail_only_goes_to_file()
    {
        using var log = TestLog.Create();
        var echoed = new List<string>();
        log.Echo += echoed.Add;

        log.Info("hello");
        log.Detail("restore output");
        log.Warn("careful");
        log.Error("broke", new InvalidOperationException("boom"));

        await Assert.That(echoed).IsEquivalentTo(["hello", "Warning: careful", "Error: broke: boom"]);
        var text = ReadShared(log.FilePath);
        await Assert.That(text).Contains("DETAIL restore output");
        await Assert.That(text).Contains("WARN   careful");
        await Assert.That(text).Contains("System.InvalidOperationException: boom");
    }

    [Test]
    public async Task Is_flushed_before_dispose()
    {
        var log = TestLog.Create();
        log.Detail("still running");

        await Assert.That(ReadShared(log.FilePath)).Contains("still running");
        log.Dispose();
        await Assert.That(ReadShared(log.FilePath)).Contains("Finished after");
    }

    [Test]
    public async Task Multi_line_messages_keep_the_prefix_on_every_line()
    {
        using var log = TestLog.Create();

        log.Detail("first\r\nsecond");

        var lines = ReadShared(log.FilePath).Split(Environment.NewLine);
        await Assert.That(lines.Any(l => l.EndsWith("DETAIL first"))).IsTrue();
        await Assert.That(lines.Any(l => l.EndsWith("DETAIL second"))).IsTrue();
    }

    [Test]
    public async Task Progress_is_clamped_reported_and_logged()
    {
        using var log = TestLog.Create();
        var updates = new List<ProgressUpdate>();
        log.Progressed += updates.Add;

        log.Progress(0.5, "Halfway");
        log.Progress(1.7, "Over");

        await Assert.That(updates).IsEquivalentTo([new ProgressUpdate(0.5, "Halfway"), new ProgressUpdate(1, "Over")]);
        await Assert.That(ReadShared(log.FilePath)).Contains("Halfway");
    }

    [Test]
    public async Task Time_logs_duration()
    {
        using var log = TestLog.Create();

        using (log.Time("restore Domain")) { }

        await Assert.That(ReadShared(log.FilePath)).Contains("restore Domain: 0.0s");
    }

    [Test]
    public async Task DefaultPath_is_timestamped_log_in_given_directory()
    {
        var path = RunLog.DefaultPath("audit", "/tmp/logs");

        await Assert.That(Path.GetDirectoryName(path)).IsEqualTo(Path.GetDirectoryName(Path.Combine("/tmp/logs", "x")));
        await Assert.That(Path.GetFileName(path)).EndsWith("-audit.log");
    }
}
