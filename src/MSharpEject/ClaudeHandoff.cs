using System.Diagnostics;
using System.Text;
using MSharpEject.Audit;

namespace MSharpEject;

/// <summary>Hands the rest of the migration to Claude Code in a terminal, with what the audit found as the brief.</summary>
public static class ClaudeHandoff
{
    public const string InstallUrl = "https://docs.claude.com/en/docs/claude-code/setup";

    /// <param name="cli">This tool's command line, so Claude can re-run the audit to check its work. Null when it can't be found.</param>
    public static string Prompt(AuditResult r, string? cli)
    {
        var text = new StringBuilder();
        text.AppendLine($"""
            Finish migrating the repository in {r.Repo} off M# and the Olive framework (Geeks Ltd), so it is plain C# and ASP.NET Core.

            Done means:
            - No M#, MSharp or Olive left anywhere: package references, project references, `using` directives, namespaces, types, base classes, attributes, Razor directives and tag helpers, npm packages (such as Olive.MvcJs), config, CI, Dockerfiles, scripts, docs and tests. A case-insensitive search for "olive", "msharp" and "geeks" outside .git should find nothing that refers to them.
            - `dotnet build` passes and `dotnet test` passes with at least the tests that passed before you started. Run both first to get the baseline.
            - Behaviour is unchanged. The database schema stays as it is: map the existing tables, don't migrate data.
            - It builds and runs on Linux: no Windows-only APIs, executables, drive-letter paths, or fonts and licence files that only exist on one machine.

            How to work:
            - Make a new git branch, and commit after each area with the build and tests green.
            - Olive is GPL v3. Never copy its source. Write replacements from documented behaviour, or use plain .NET and mainstream libraries (EF Core or ADO.NET for data, ASP.NET Core MVC for the web layer, small hand-written scripts for the front end).
            - Go in this order: service integrations, core extensions, MVC, front end, data layer, then remove the package references.
            - If something can't be done without changing behaviour, stop and ask.
            """);

        text.AppendLine().AppendLine("What the last audit found:");
        foreach (var verdict in Remaining.Verdicts(r)) text.AppendLine($"- {verdict}");
        foreach (var item in Remaining.Items(r)) text.AppendLine($"- {item.Kind} · {item.Item}: {item.Found}. {item.ManualWork}");
        if (r.Semantic)
        {
            var top = r.Symbols.Take(40).Select(s => $"{s.Symbol} ({s.Count})");
            if (r.Symbols.Count > 0) text.AppendLine().AppendLine($"Most used Olive symbols: {string.Join(", ", top)}.");
        }

        if (cli != null)
            text.AppendLine().AppendLine($"To check your work, run `\"{cli}\" audit \"{r.Repo}\"` and repeat until it says Olive-free: yes and lists nothing left.");
        return text.ToString();
    }

    public static string? FindClaude()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "claude.exe", "claude.cmd", "claude.bat" } : ["claude"];
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));
        return dirs.SelectMany(d => names.Select(n => Path.Combine(d, n))).FirstOrDefault(File.Exists);
    }

    /// <summary>Writes a launcher script next to the prompt and returns how to open it in a new terminal window.</summary>
    public static ProcessStartInfo Terminal(string repo, string promptFile)
    {
        var dir = Path.GetDirectoryName(promptFile)!;
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(dir, "claude-handoff.ps1");
            File.WriteAllText(script, $"Set-Location -LiteralPath {Ps(repo)}\nclaude (Get-Content -Raw -LiteralPath {Ps(promptFile)})\n", new UTF8Encoding(true));
            // start gives the terminal its own window; the GUI has no console to share.
            return new ProcessStartInfo("cmd.exe", $"/c start \"Claude Code\" powershell -NoExit -ExecutionPolicy Bypass -File \"{script}\"") { UseShellExecute = false, CreateNoWindow = true };
        }

        var sh = Path.Combine(dir, OperatingSystem.IsMacOS() ? "claude-handoff.command" : "claude-handoff.sh");
        File.WriteAllText(sh, $"#!/bin/bash\ncd {Sh(repo)} && claude \"$(cat {Sh(promptFile)})\"\nexec bash\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (OperatingSystem.IsMacOS()) return new ProcessStartInfo("open", $"-a Terminal \"{sh}\"") { UseShellExecute = false };

        var terminal = new[] { ("x-terminal-emulator", "-e"), ("gnome-terminal", "--"), ("konsole", "-e"), ("xterm", "-e") }
            .FirstOrDefault(t => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(d => File.Exists(Path.Combine(d, t.Item1))));
        if (terminal.Item1 == null) throw new InvalidOperationException("No terminal emulator found. Run: " + sh);
        return new ProcessStartInfo(terminal.Item1, $"{terminal.Item2} bash \"{sh}\"") { UseShellExecute = false };
    }

    static string Ps(string s) => "'" + s.Replace("'", "''") + "'";
    static string Sh(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
