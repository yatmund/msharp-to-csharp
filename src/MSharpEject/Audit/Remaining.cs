namespace MSharpEject.Audit;

public record LeftItem(string Kind, string Item, string Found, string Tool, string ManualWork);

public static class Remaining
{
    record Area(string Name, string Tool, string ManualWork);

    static readonly Area Build = new("M# build and tooling", "eject removes it", "None after eject.");
    static readonly Area Data = new("Data layer", "de-olive moves it to the Platform library, generating the same SQL as Olive",
        "Run it against your database. Encrypted properties aren't supported, and the data layer stays Olive-shaped until you move it to EF Core.");
    static readonly Area Mvc = new("MVC", "de-olive moves it to the Platform library",
        "Click through the app. Binding, validation, routing, files and controller JSON are checked against Olive; Razor views aren't.");
    static readonly Area FrontEnd = new("Front end", "de-olive replaces Olive.MvcJs with the Platform front end",
        "Click through the app, and run your build's yarn install so pako lands in wwwroot/lib.");
    static readonly Area Services = new("Service integrations", "de-olive replaces Audit, CSV, Export, Email, ApiClient, Microservices and Security",
        "Replace the rest (storage, background tasks, PDF, drawing) with a maintained library or your own code.");
    static readonly Area Core = new("Core extensions and runtime", $"de-olive rewrites {DeOlive.BclRules.All.Length} extension calls with exact .NET equivalents",
        "Rewrite the remaining extensions, Config, Context and Log calls by hand.");
    static readonly Area Other = new("Other Geeks packages", "Not migrated", "Replace or remove by hand.");

    static readonly Area[] Order = [Build, Data, Mvc, FrontEnd, Services, Core, Other];

    static Area AreaOf(string id, bool npm = false)
    {
        bool Is(string prefix) => id.Equals(prefix, StringComparison.OrdinalIgnoreCase) || id.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);
        if (npm) return FrontEnd;
        if (Is("MSharp") || Is("msharp-build") || Is("GCop")) return Build;
        if (Is("Olive.Mvc.Security")) return Services;
        if (Is("Olive.Entities")) return Data;
        if (Is("Olive.Mvc") || Is("Olive.Web")) return Mvc;
        if (id.Equals("Olive", StringComparison.OrdinalIgnoreCase)) return Core;
        return Is("Olive") ? Services : Other;
    }

    public static string AreaName(string assembly) => AreaOf(assembly).Name;

    public static readonly Dictionary<string, string> RuleNotes = new()
    {
        ["build-exec"] = "Build runs a Windows or Geeks command. Replace with a cross-platform step.",
        ["build-task-exe"] = "Build loads a task from an .exe. Replace with a cross-platform step.",
        ["windows-api"] = "Windows-only .NET API (System.Drawing, registry, P/Invoke, WinForms, WMI, event log, COM). Throws PlatformNotSupportedException on Linux.",
        ["windows-path"] = "Drive letter or backslash path. Use Path.Combine and configuration.",
        ["windows-exe"] = "Runs or ships a Windows executable, such as a document converter. Needs a Linux build of the tool, or a library or service instead.",
        ["license-file"] = "Loads a licence file. Check it ships with the deployment and the path resolves on Linux.",
        ["font"] = "Depends on installed fonts. Linux images have few; install them in the image or ship the font files.",
        ["eol-framework"] = "Out-of-support .NET. Current Linux images and patches need a supported version.",
        ["container-image"] = "Windows or retired container image.",
        ["geeks-reference"] = "Config or CI points at Geeks servers or tools.",
        ["iis-config"] = "IIS-only config; Kestrel on Linux ignores it.",
        ["windows-script"] = "Windows batch script. Replace with a shell script or dotnet command.",
    };

    public static List<LeftItem> Items(AuditResult r)
    {
        var items = new List<LeftItem>();
        foreach (var area in Order)
        {
            var packages = r.Packages.Where(p => AreaOf(p.Id, p.Project.EndsWith("package.json", StringComparison.OrdinalIgnoreCase)) == area)
                .Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            var symbols = r.Symbols.Where(s => AreaOf(s.Assembly) == area).ToList();
            if (packages.Count == 0 && symbols.Count == 0) continue;

            var found = new List<string>();
            if (packages.Count > 0) found.Add(string.Join(", ", packages));
            if (symbols.Count > 0)
                found.Add($"{symbols.Sum(s => s.Count)} uses of {symbols.Count} symbols ({symbols.Sum(s => s.Count - s.GeneratedCount)} hand-written)");
            items.Add(new("Olive", area.Name, string.Join("; ", found), area.Tool, area.ManualWork));
        }

        foreach (var group in r.BuildSteps.Concat(r.Blockers).GroupBy(f => f.Rule).OrderBy(g => g.Key, StringComparer.Ordinal))
            items.Add(new("Linux", group.Key, $"{group.Count()} in {group.Select(f => f.File).Distinct().Count()} files",
                group.Key.StartsWith("build-") || group.Key is "container-image" or "windows-script" ? "eject fixes the M# template's own" : "Not migrated",
                RuleNotes.GetValueOrDefault(group.Key, "")));
        return items;
    }

    public static string[] Verdicts(AuditResult r)
    {
        var packages = r.Packages.Count(p => AreaOf(p.Id) != Build);
        var uses = r.Symbols.Sum(s => s.Count);
        var oliveFree = packages == 0 && (!r.Semantic || uses == 0);
        var olive = oliveFree
            ? r.Semantic ? "Olive-free: yes." : "Olive-free: unconfirmed. No Olive package references left, but symbols weren't resolved."
            : $"Olive-free: no. {packages} Geeks package references" + (r.Semantic ? $" and {uses} Olive symbol uses" : "") + " remain.";

        var platform = r.BuildSteps.Count + r.Blockers.Count;
        var linux = platform == 0
            ? "Linux-ready: no blockers found by the scans."
            : $"Linux-ready: no. {r.BuildSteps.Count} build steps and {r.Blockers.Count} deployment blockers remain.";
        return [olive, linux];
    }

    public static string ToText(AuditResult r) =>
        "Still left:\n" + string.Join("\n", Verdicts(r).Select(v => "  " + v)) + "\n"
        + string.Concat(Items(r).Select(i => $"  {i.Kind} · {i.Item}: {i.Found}. {i.ManualWork}\n"))
        + (r.Semantic ? "" : "  Olive symbol uses aren't counted here; `msharp-eject audit` lists them.\n");
}
