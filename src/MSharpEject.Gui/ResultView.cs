using MSharpEject.Audit;

namespace MSharpEject.Gui;

public record DslRow(string Kind, int Count);

public record Stat(string Label, string Value, bool Attention, string? Hint = null);

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
