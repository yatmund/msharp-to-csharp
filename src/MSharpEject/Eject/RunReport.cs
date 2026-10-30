using System.Text;

namespace MSharpEject.Eject;

public static class RunReport
{
    public static string ToText(string command, RunOutcome outcome, RunOptions options)
    {
        var text = new StringBuilder();
        var verb = options.DryRun ? "would make" : outcome.Applied ? "made" : "planned";
        text.AppendLine($"{command}: {verb} {outcome.Changes.Count} changes");

        foreach (var group in outcome.Changes.GroupBy(c => c.Action))
        {
            text.AppendLine($"  {group.Key}:");
            foreach (var change in group) text.AppendLine($"    {change.Path}  ({change.Reason})");
        }

        if (outcome.Baseline != null && outcome.After != null)
            text.AppendLine($"Verification: before {Describe(outcome.Baseline)}; after {Describe(outcome.After)}");

        foreach (var note in outcome.Notes) text.AppendLine("Note: " + note);
        text.AppendLine(outcome.Verdict == null
            ? options.DryRun ? "Dry run only: nothing was changed." : outcome.Applied ? "OK. Review with `git diff`, then commit." : "Nothing to do."
            : "PROBLEM: " + outcome.Verdict + (outcome.Applied ? " Undo with `git checkout -- .`" : ""));
        return text.ToString();
    }

    public static string Describe(VerifyResult r) =>
        !r.Built ? "build failed"
        : !r.HasTests ? "builds, no tests"
        : $"builds, {r.Passed} passed, {r.FailedTests.Count} failed";
}
