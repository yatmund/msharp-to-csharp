using MSharpEject.Audit;

const string usage = """
    Usage:
      msharp-eject audit <repo> [--out <file>] [--no-semantic]
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

var outIndex = Array.IndexOf(args, "--out");
var outFile = outIndex >= 0 && outIndex + 1 < args.Length ? args[outIndex + 1] : null;

var result = await Auditor.RunAsync(repo, semantic: !args.Contains("--no-semantic"), Console.Error.WriteLine);
var markdown = Auditor.ToMarkdown(result);

if (outFile == null) Console.WriteLine(markdown);
else
{
    File.WriteAllText(outFile, markdown);
    Console.Error.WriteLine($"Wrote {outFile}");
}
return 0;
