using MSharpEject;
using MSharpEject.Audit;
using MSharpEject.DeOlive;
using MSharpEject.Eject;

const string usage = """
    Usage:
      msharp-eject audit    <repo> [--out <file>] [--no-semantic]
      msharp-eject eject    <repo> [--dry-run] [--allow-dirty] [--no-verify] [--generate]
      msharp-eject de-olive <repo> [--dry-run] [--allow-dirty] [--no-verify]

    Every command also takes --log <file>.

      audit      Read-only report of what ties the repo to M# and Olive.
      eject      Commit M#'s generated code as source, delete M#, make the build cross-platform.
      de-olive   Rewrite Olive calls that have exact .NET equivalents.

      --dry-run      List the changes without making them.
      --allow-dirty  Run even with uncommitted changes (they can't be told apart afterwards).
      --no-verify    Skip building and testing before and after.
      --generate     Run M# once to produce the generated code first (Windows only).
    """;

string[] commands = ["audit", "eject", "de-olive"];
if (args.Length < 2 || !commands.Contains(args[0]))
{
    Console.Error.WriteLine(usage);
    return 1;
}

var command = args[0];
var repo = args[1];
if (!Directory.Exists(repo))
{
    Console.Error.WriteLine($"Not a directory: {repo}");
    return 1;
}

string? Option(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
bool Flag(string name) => args.Contains(name);

using var log = new RunLog(Option("--log") ?? RunLog.DefaultPath(command), command);
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
    if (command == "audit")
    {
        var result = await Auditor.RunAsync(repo, semantic: !Flag("--no-semantic"), log, cancel.Token);
        var markdown = Auditor.ToMarkdown(result);
        if (Option("--out") is not { } outFile) Console.WriteLine(markdown);
        else
        {
            File.WriteAllText(outFile, markdown);
            log.Info($"Wrote {Path.GetFullPath(outFile)}");
        }
        return 0;
    }

    var options = new RunOptions(Flag("--dry-run"), Flag("--allow-dirty"), !Flag("--no-verify"), Flag("--generate"));
    var outcome = command == "eject"
        ? await Ejector.RunAsync(repo, options, log, cancel.Token)
        : await DeOliver.RunAsync(repo, options, log, cancel.Token);

    Console.WriteLine(RunReport.ToText(command, outcome, options));
    return outcome.Verdict == null ? 0 : 3;
}
catch (OperationCanceledException)
{
    log.Warn("Cancelled. Files may be partly changed; `git checkout -- .` undoes everything.");
    return 130;
}
catch (Exception ex)
{
    log.Error($"{command} failed", ex);
    Console.Error.WriteLine($"Full details: {log.FilePath}");
    return 2;
}
