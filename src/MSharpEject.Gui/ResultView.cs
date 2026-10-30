using MSharpEject.Audit;
using MSharpEject.Eject;

namespace MSharpEject.Gui;

public record DslRow(string Kind, int Count);

public record ChangeRow(string Action, string Path, string Reason);

public record Stat(string Label, string Value, bool Attention, string? Hint = null);

public enum BannerKind { Info, Ok, Problem }

public record Banner(string Text, BannerKind Kind, bool CanApply);

public static class ResultView
{
    public static List<T> Filter<T>(IEnumerable<T> items, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return items.ToList();
        var properties = typeof(T).GetProperties();
        return items
            .Where(item => properties.Any(p => p.GetValue(item)?.ToString()?.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase) == true))
            .ToList();
    }

    public static string Header(string name, int shown, int total) =>
        shown == total ? $"{name} ({total})" : $"{name} ({shown}/{total})";

    public static List<DslRow> DslRows(AuditResult result) =>
        result.Dsl.OrderByDescending(d => d.Value).Select(d => new DslRow(d.Key, d.Value)).ToList();

    public static List<ChangeRow> ChangeRows(RunOutcome outcome) =>
        outcome.Changes.Select(c => new ChangeRow(c.Action, c.Path, c.Reason)).ToList();

    public static string Title(string command) => command == "eject" ? "Eject M#" : "Replace Olive";

    public static Banner Banner(string command, RunOutcome outcome, bool preview)
    {
        var title = Title(command);
        var notes = outcome.Notes.Count == 0 ? "" : " " + string.Join(" ", outcome.Notes);
        if (outcome.Verdict != null)
            return new(outcome.Applied
                ? $"{title} applied {outcome.Changes.Count} changes, but: {outcome.Verdict} Review with git diff; undo with git checkout -- .{notes}"
                : $"{title} can't run: {outcome.Verdict}{notes}", BannerKind.Problem, false);
        if (outcome.Changes.Count == 0)
            return new($"{title}: nothing to change.{notes}", BannerKind.Ok, false);
        if (preview)
            return new($"{title} will make {outcome.Changes.Count} changes, listed below. Nothing has been changed yet.{notes}", BannerKind.Info, true);

        var verified = outcome.After is { } after
            ? $" Verified: {RunReport.Describe(after)}" + (outcome.Baseline is { } before ? $" (before: {RunReport.Describe(before)})." : ".")
            : " Not verified (build and test was off).";
        return new($"{title} applied {outcome.Changes.Count} changes.{verified} Review with git diff, then commit.{notes}", BannerKind.Ok, false);
    }

    public static List<Stat> OutcomeStats(RunOutcome outcome)
    {
        var stats = new List<Stat>
        {
            new("Changes", outcome.Changes.Count.ToString(), false),
            new("Files edited", outcome.Changes.OfType<EditFile>().Count().ToString(), false),
            new("Deleted", (outcome.Changes.Count - outcome.Changes.OfType<EditFile>().Count()).ToString(), false, "files and folders"),
        };
        if (outcome.After is { } after)
        {
            stats.Add(new("Build", after.Built ? "OK" : "Failed", !after.Built));
            if (after.HasTests)
                stats.Add(new("Tests", $"{after.Passed}/{after.Passed + after.FailedTests.Count}", after.FailedTests.Count > 0,
                    outcome.Baseline is { HasTests: true } b ? $"before: {b.Passed}/{b.Passed + b.FailedTests.Count}" : null));
        }
        return stats;
    }

    public static List<Stat> Stats(AuditResult r)
    {
        var packages = r.Packages.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var definitions = r.Dsl.Where(d => d.Key != "c# expressions").Sum(d => d.Value);
        var generatedFiles = r.Generated.Sum(g => g.Files);
        var newestGenerated = r.Generated.Max(g => g.Newest);
        var generatedHint = newestGenerated == null ? "M# must run once first"
            : r.NewestDslWrite > newestGenerated ? "Older than the definitions"
            : null;

        var olive = r.Semantic
            ? new Stat("Olive symbols", r.Symbols.Count.ToString(), r.Symbols.Count > 0,
                $"{r.Symbols.Sum(s => s.Count - s.GeneratedCount)} hand-written uses")
            : new Stat("Olive symbols", "–", false, "Not resolved");

        return
        [
            new("Geeks packages", packages.ToString(), packages > 0),
            new("M# definitions", definitions.ToString(), false),
            new("Generated files", generatedFiles.ToString(), generatedHint != null, generatedHint),
            new("Build steps", r.BuildSteps.Count.ToString(), r.BuildSteps.Count > 0),
            new("Deployment blockers", r.Blockers.Count.ToString(), r.Blockers.Count > 0),
            olive with { Hint = r.LoadErrors.Count > 0 ? $"{r.LoadErrors.Count} load errors, see log" : olive.Hint },
        ];
    }
}
