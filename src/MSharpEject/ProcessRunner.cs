using System.Diagnostics;

namespace MSharpEject;

public record ProcessResult(int ExitCode, string Output, string Error);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string exe, string args, string cwd, RunLog log, CancellationToken cancel = default)
    {
        log.Detail($"> {exe} {args}  (in {cwd})");
        using var process = Process.Start(new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException($"Could not start {exe}");

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
}
