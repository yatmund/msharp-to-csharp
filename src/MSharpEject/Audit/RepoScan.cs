using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MSharpEject.Audit;

public record PackageRef(string Project, string Id, string? Version);

public record Finding(string File, int Line, string Rule, string Text);

public record GeneratedFolder(string Path, int Files, DateTime? Newest);

public static partial class RepoScan
{
    static readonly string[] SkippedDirs = ["bin", "obj", "node_modules", ".git", ".vs", "packages", "wwwroot/lib"];

    public static readonly string[] GeneratedFolderPaths =
    [
        "Domain/[GEN-Entities]", "Domain/[GEN-DAL]",
        "Website/Controllers/Pages", "Website/Controllers/Modules",
        "Website/Views/Pages", "Website/Views/Modules",
        "DB/Tables",
    ];

    static readonly string[] DslBaseTypes =
        ["EntityType", "SubType", "ListModule", "FormModule", "ViewModule", "GenericModule", "MenuModule", "RootPage", "SubPage"];

    public static bool IsGeeksPackage(string id) =>
        new[] { "Olive", "MSharp", "GCop", "Geeks", "Zebble", "Pangolin", "msharp-build" }
            .Any(p => id.Equals(p, StringComparison.OrdinalIgnoreCase) || id.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<PackageRef> GeeksPackages(string project, string csprojXml) =>
        from e in XDocument.Parse(csprojXml).Descendants()
        where e.Name.LocalName == "PackageReference"
        let id = (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update")
        where id != null && IsGeeksPackage(id)
        let version = (string?)e.Attribute("Version") ?? e.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value
        select new PackageRef(project, id, version);

    public static IEnumerable<PackageRef> GeeksTools(string manifest, string json)
    {
        // dotnet-tools.json: tool ids are the keys under "tools".
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("tools", out var tools)) yield break;
        foreach (var tool in tools.EnumerateObject().Where(t => IsGeeksPackage(t.Name)))
            yield return new PackageRef(manifest, tool.Name, tool.Value.TryGetProperty("version", out var v) ? v.GetString() : null);
    }

    public static IEnumerable<PackageRef> GeeksNpmPackages(string packageJsonPath, string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        foreach (var section in new[] { "dependencies", "devDependencies" })
        {
            if (!doc.RootElement.TryGetProperty(section, out var deps)) continue;
            foreach (var dep in deps.EnumerateObject())
            {
                var spec = dep.Value.GetString() ?? "";
                if (IsGeeksPackage(dep.Name) || spec.Contains("geeksltd", StringComparison.OrdinalIgnoreCase))
                    yield return new PackageRef(packageJsonPath, dep.Name, spec);
            }
        }
    }

    public static IEnumerable<Finding> BuildSteps(string project, string csprojXml)
    {
        foreach (var e in XDocument.Parse(csprojXml, LoadOptions.SetLineInfo).Descendants())
        {
            var line = ((System.Xml.IXmlLineInfo)e).LineNumber;
            if (e.Name.LocalName == "Exec" && (string?)e.Attribute("Command") is { } cmd && WindowsOrGeeksCommand().IsMatch(cmd))
                yield return new Finding(project, line, "build-exec", cmd);
            if (e.Name.LocalName == "UsingTask" && (string?)e.Attribute("AssemblyFile") is { } asm && asm.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                yield return new Finding(project, line, "build-task-exe", asm);
        }
    }

    public static Dictionary<string, int> DslInventory(IEnumerable<string> sources)
    {
        var counts = new Dictionary<string, int>();
        foreach (var source in sources)
        {
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var kind = type.BaseList?.Types.Select(t => BaseName(t.Type)).FirstOrDefault(DslBaseTypes.Contains);
                if (kind != null) counts[kind] = counts.GetValueOrDefault(kind) + 1;
            }

            var inlineCSharp = root.DescendantTokens().Count(t => t.IsKind(SyntaxKind.StringLiteralToken) && t.ValueText.StartsWith("c#:"));
            if (inlineCSharp > 0) counts["c# expressions"] = counts.GetValueOrDefault("c# expressions") + inlineCSharp;
        }
        return counts;
    }

    static string BaseName(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax q => BaseName(q.Right),
        GenericNameSyntax g => g.Identifier.Text,
        SimpleNameSyntax s => s.Identifier.Text,
        _ => type.ToString(),
    };

    public static IEnumerable<Finding> DeploymentBlockers(string file, string text)
    {
        var name = Path.GetFileName(file);
        var ext = Path.GetExtension(file).ToLowerInvariant();
        var rules = new List<(string Rule, Regex Pattern)>();

        if (ext == ".cs")
        {
            rules.Add(("windows-api", WindowsApi()));
            rules.Add(("windows-path", WindowsPath()));
        }
        if (name.StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase))
            rules.Add(("windows-container", WindowsContainer()));
        if (ext is ".json" or ".yml" or ".yaml" or ".config" or ".ps1" or ".bat" or ".cmd" or ".xml" || name == "Jenkinsfile")
            rules.Add(("geeks-reference", GeeksReference()));

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            foreach (var (rule, pattern) in rules)
                if (pattern.IsMatch(lines[i]))
                    yield return new Finding(file, i + 1, rule, lines[i].Trim());

        if (name.Equals("web.config", StringComparison.OrdinalIgnoreCase))
            yield return new Finding(file, 1, "iis-config", "IIS-only config; Kestrel ignores it");
        if (ext is ".bat" or ".cmd")
            yield return new Finding(file, 1, "windows-script", name);
    }

    public static GeneratedFolder ScanGeneratedFolder(string repo, string relative)
    {
        var dir = Path.Combine(repo, relative);
        if (!Directory.Exists(dir)) return new GeneratedFolder(relative, 0, null);
        // DB/Tables ships a committed -Warning!.txt; only code counts.
        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".cs" or ".cshtml" or ".sql")
            .ToArray();
        return new GeneratedFolder(relative, files.Length, files.Length == 0 ? null : files.Max(File.GetLastWriteTimeUtc));
    }

    public static IEnumerable<string> RepoFiles(string repo) =>
        Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories)
            .Where(f => !IsSkipped(Path.GetRelativePath(repo, f)));

    static bool IsSkipped(string relative)
    {
        var path = relative.Replace('\\', '/');
        return SkippedDirs.Any(d => path.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase) || path.Contains("/" + d + "/", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsDslFile(string relative) =>
        relative.Replace('\\', '/').StartsWith("M#/", StringComparison.OrdinalIgnoreCase);

    public static bool IsGeneratedFile(string relative)
    {
        var path = relative.Replace('\\', '/');
        return GeneratedFolderPaths.Any(g => path.StartsWith(g + "/", StringComparison.OrdinalIgnoreCase))
            || path.EndsWith("app_Start/TaskManager.cs", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"msharp|\bstart\s|\.bat\b|\.cmd\b|\.exe\b", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsOrGeeksCommand();

    [GeneratedRegex(@"\bSystem\.Drawing\b|\bMicrosoft\.Win32\b|\[DllImport|\bRegistry\.|\bSystem\.Windows\.Forms\b|\bSystem\.Management\b")]
    private static partial Regex WindowsApi();

    // Drive letters, or a backslash between path segments inside a regular string literal.
    [GeneratedRegex(@"""[A-Za-z]:\\|@""[^""]*\w\\\w|""[^""\n]*\w\\\\\w[^""\n]*""")]
    private static partial Regex WindowsPath();

    [GeneratedRegex(@"^\s*FROM\s+\S*(nanoserver|windowsservercore|servercore|windows)", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsContainer();

    [GeneratedRegex(@"geeks\.ltd|geeksltd|msharp\.co\.uk|\bmsharp\b", RegexOptions.IgnoreCase)]
    private static partial Regex GeeksReference();
}
