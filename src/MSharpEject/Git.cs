namespace MSharpEject;

public static class Git
{
    /// <returns>Null when the tree is clean, otherwise why it isn't safe to change.</returns>
    public static async Task<string?> WhyNotCleanAsync(string repo, RunLog log, CancellationToken cancel)
    {
        ProcessResult status;
        try
        {
            status = await ProcessRunner.RunAsync("git", "status --porcelain", repo, log, cancel);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "git isn't installed or isn't on PATH.";
        }

        if (status.ExitCode != 0) return "Not a git repository. Changes can't be undone without one.";
        return string.IsNullOrWhiteSpace(status.Output)
            ? null
            : "The working tree has uncommitted changes. Commit or stash them first, so every change this tool makes can be reviewed and undone.";
    }
}
