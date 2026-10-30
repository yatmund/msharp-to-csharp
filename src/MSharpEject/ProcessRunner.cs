using System.Diagnostics;

namespace MSharpEject;

public record ProcessResult(int ExitCode, string Output, string Error);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string exe, string args, string cwd, RunLog log, CancellationToken cancel = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        log.Detail($"> {exe} {args}  (in {cwd})");
        using var process = Process.Start(CreateStartInfo(exe, args, cwd, environment)) ?? throw new InvalidOperationException($"Could not start {exe}");
        // Nothing should wait on input (a git credential prompt, say); EOF makes it fail instead of hang.
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            log.Detail($"Killed {exe} on cancel");
            throw;
        }

        var result = new ProcessResult(process.ExitCode, await output, await error);
        log.Detail($"Exit code {result.ExitCode}");
        if (result.Output.Length > 0) log.Detail("stdout:\n" + result.Output);
        if (result.Error.Length > 0) log.Detail("stderr:\n" + result.Error);
        return result;
    }

    public static ProcessStartInfo CreateStartInfo(string exe, string args, string cwd, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = cwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // The GUI has no console, so on Windows each console child would otherwise open its own window.
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        // MSBuild worker nodes otherwise outlive the run and hold memory and file locks.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
            start.Environment[key] = value;
        return start;
    }
}
