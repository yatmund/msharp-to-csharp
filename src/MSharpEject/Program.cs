using MSharpEject;
using MSharpEject.Audit;

const string usage = """
    Usage:
      msharp-eject audit <repo> [--out <file>] [--log <file>] [--no-semantic]
    """;

if (args.Length < 2 || args[0] != "audit")
{
    Console.Error.WriteLine(usage);
    return 1;
}

var repo = args[1];
if (!Directory.Exists(repo))
{
    Console.Error.WriteLine($"Not a directory: {repo}");
    return 1;
}

string? Option(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
var outFile = Option("--out");

using var log = new RunLog(Option("--log") ?? RunLog.DefaultPath("audit"), "audit");
log.Echo += Console.Error.WriteLine;
Console.Error.WriteLine($"Log: {log.FilePath}");

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

try
{
    var result = await Auditor.RunAsync(repo, semantic: !args.Contains("--no-semantic"), log, cancel.Token);
    var markdown = Auditor.ToMarkdown(result);

    if (outFile == null) Console.WriteLine(markdown);
    else
    {
        File.WriteAllText(outFile, markdown);
        log.Info($"Wrote {Path.GetFullPath(outFile)}");
    }
    return 0;
}
catch (OperationCanceledException)
{
    log.Warn("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    log.Error("Audit failed", ex);
    Console.Error.WriteLine($"Full details: {log.FilePath}");
    return 2;
}
