using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MSharpEject.Audit;

namespace MSharpEject.Eject;

public record SassPair(string Input, string Output);

public record FrontEnd(bool HasPackageJson, bool UsesYarn, bool HasTsConfig, IReadOnlyList<SassPair> Sass);

/// <summary>Pure text transforms. Each returns null when there's nothing to change.</summary>
public static partial class EjectTransforms
{
    public const string SassVersion = "1.107.0";
    public const string YarnVersion = "1.22.22";
    public const string TypeScriptVersion = "5.9.3";

    static readonly string[] IgnoredGeneratedPaths =
    [
        "domain/[gen-", "website/views/pages", "website/views/modules",
        "website/controllers/pages", "website/controllers/modules",
        "website/app_start/taskmanager.cs", "m#/", "@m#/",
    ];

    public static string? Gitignore(string text)
    {
        var newLine = FileText.NewLineOf(text);
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var kept = lines.Where(l => !IgnoresGeneratedOrMSharp(l)).ToList();
        return kept.Count == lines.Count ? null : string.Join(newLine, kept);
    }

    static bool IgnoresGeneratedOrMSharp(string line)
    {
        var pattern = line.Trim().TrimStart('!', '/').Replace("\\[", "[").Replace("\\]", "]").Replace('\\', '/').ToLowerInvariant();
        return pattern.Length > 0 && !pattern.StartsWith('#') && IgnoredGeneratedPaths.Any(pattern.StartsWith);
    }

    public static string? Sln(string text) => SlnWithout(text, IsDslPath);

    public static string? RemoveOliveFromSln(string text) =>
        SlnWithout(text, p => Replacement.ReplacementLibrary.IsOliveAssembly(Path.GetFileNameWithoutExtension(p.Replace('\\', '/'))));

    static string? SlnWithout(string text, Func<string, bool> drop)
    {
        var newLine = FileText.NewLineOf(text);
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var project = SlnProject().Match(lines[i]);
            if (project.Success && drop(project.Groups["path"].Value))
            {
                guids.Add(project.Groups["guid"].Value);
                while (i < lines.Count && lines[i].Trim() != "EndProject") i++;
                continue;
            }
            kept.Add(lines[i]);
        }

        if (guids.Count == 0) return null;
        kept = kept.Where(l => !guids.Any(g => l.Contains(g, StringComparison.OrdinalIgnoreCase))).ToList();
        return string.Join(newLine, kept);
    }

    public static string? Slnx(string xml)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var dsl = doc.Descendants().Where(e => e.Name.LocalName == "Project" && IsDslPath((string?)e.Attribute("Path") ?? "")).ToList();
        if (dsl.Count == 0) return null;
        dsl.ForEach(RemoveWithIndent);
        return Serialize(doc, xml);
    }

    static bool IsDslPath(string path)
    {
        var p = path.Replace('\\', '/');
        return RepoScan.IsDslFile(p) || p.StartsWith("@M#/", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(p).StartsWith('#');
    }

    public static string? ToolsManifest(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject();
        if (root?["tools"] is not JsonObject tools) return null;
        var geeks = tools.Select(t => t.Key).Where(RepoScan.IsGeeksPackage).ToList();
        if (geeks.Count == 0) return null;
        geeks.ForEach(id => tools.Remove(id));
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <param name="dotnetVersion">"8.0" etc., from the project the Dockerfile builds.</param>
    public static string? Dockerfile(string text, string dotnetVersion)
    {
        var replaced = DockerFrom().Replace(text, match =>
        {
            var image = match.Groups["image"].Value.ToLowerInvariant();
            var tag = match.Groups["tag"].Value.ToLowerInvariant();
            var kind =
                image is "microsoft/aspnetcore-build" || image.EndsWith("/sdk") || (image == "microsoft/dotnet" && tag.Contains("sdk")) ? "sdk"
                : image is "microsoft/aspnetcore" || image.EndsWith("/aspnet") || (image == "microsoft/dotnet" && tag.Contains("aspnetcore")) ? "aspnet"
                : image.EndsWith("/runtime") || image == "microsoft/dotnet" ? "runtime"
                : null;

            var retired = image.StartsWith("microsoft/") || image.StartsWith("mcr.microsoft.com/dotnet/core/");
            var windows = tag.Contains("nanoserver") || tag.Contains("windowsservercore");
            return kind != null && (retired || windows)
                ? $"{match.Groups["from"].Value}mcr.microsoft.com/dotnet/{kind}:{dotnetVersion}"
                : match.Value;
        });
        return replaced == text ? null : replaced;
    }

    public static string? DotnetVersion(string csprojXml) =>
        TargetFramework().Match(csprojXml) is { Success: true } m ? m.Groups["version"].Value : null;

    public static IReadOnlyList<SassPair> SassPairs(string compilerConfigJson) =>
        JsonNode.Parse(compilerConfigJson)?.AsArray()
            .Select(e => (Input: (string?)e?["inputFile"], Output: (string?)e?["outputFile"]))
            .Where(p => p.Input?.EndsWith(".scss", StringComparison.OrdinalIgnoreCase) == true && p.Output != null)
            .Select(p => new SassPair(p.Input!, p.Output!))
            .ToList() ?? [];

    /// <param name="frontEnd">Null for projects with no web front end next to them.</param>
    public static string? Csproj(string xml, FrontEnd? frontEnd)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var project = doc.Root!;
        var changed = false;

        foreach (var reference in project.Descendants().Where(e => e.Name.LocalName == "PackageReference" && IsMSharp(e)).ToList())
        {
            RemoveWithIndent(reference);
            changed = true;
        }

        var webCompilerTasks = project.Elements().Where(e => e.Name.LocalName == "UsingTask" && ((string?)e.Attribute("TaskName"))?.StartsWith("WebCompiler.") == true).ToList();
        var webCompilerTargets = project.Elements().Where(e => e.Name.LocalName == "Target" && e.Elements().Any(c => c.Name.LocalName.StartsWith("WebCompiler."))).ToList();
        var hadWebCompiler = webCompilerTasks.Count + webCompilerTargets.Count > 0;
        foreach (var e in webCompilerTasks.Concat(webCompilerTargets)) RemoveWithIndent(e);
        changed |= hadWebCompiler;

        if (frontEnd != null)
        {
            var indent = ChildIndent(project);
            if (frontEnd.HasPackageJson && !HasTarget(project, "RestoreFrontEnd"))
            {
                var install = frontEnd.UsesYarn ? $"npx --yes yarn@{YarnVersion} install" : "npm install";
                AddBeforeEnd(project, indent, new XComment(" Installs front-end packages on first build; needs Node.js. "));
                AddBeforeEnd(project, indent, Target("RestoreFrontEnd", "!Exists('node_modules') And !Exists('wwwroot/lib')", install, indent));
                changed = true;
            }
            if (frontEnd.Sass.Count > 0 && !HasTarget(project, "CompileSass"))
            {
                var pairs = string.Join(" ", frontEnd.Sass.Select(p => $"{p.Input}:{p.Output}"));
                var outputs = string.Join(";", frontEnd.Sass.Select(p => p.Output));
                AddBeforeEnd(project, indent, new XComment(" Compiles SCSS on build, skipped when no .scss changed; needs Node.js. "));
                AddBeforeEnd(project, indent, Target("CompileSass", "'$(SkipSass)' != 'true'", $"npx --yes sass@{SassVersion} --no-source-map --quiet {pairs}", indent,
                    inputs: "@(Content->WithMetadataValue('Extension','.scss'));@(None->WithMetadataValue('Extension','.scss'));wwwroot/**/*.scss", outputs: outputs));
                changed = true;
            }
            // Microsoft.TypeScript.MSBuild adds predicted outputs as static web assets; build fails when one is missing.
            if (frontEnd.HasTsConfig && !HasTarget(project, "CompileTypeScript"))
            {
                AddBeforeEnd(project, indent, new XComment(" Compiles TypeScript on build; needs Node.js. "));
                AddBeforeEnd(project, indent, Target("CompileTypeScript", "'$(SkipTypeScript)' != 'true'", $"npx --yes -p typescript@{TypeScriptVersion} tsc -p tsconfig.json", indent));
                changed = true;
            }
        }

        foreach (var empty in project.Elements().Where(e => e.Name.LocalName == "ItemGroup" && !e.Elements().Any()).ToList())
        {
            RemoveWithIndent(empty);
            changed = true;
        }

        return changed ? Serialize(doc, xml) : null;
    }

    /// <param name="library">Path of the replacement library's csproj relative to this project; null leaves references to it alone.</param>
    /// <param name="dropOlive">Remove Olive package references and project references to Olive projects.</param>
    public static string? OliveReferences(string xml, string? library, bool dropOlive)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var project = doc.Root!;
        var changed = false;

        if (dropOlive)
            foreach (var reference in project.Descendants().Where(IsOliveReference).ToList())
            {
                RemoveWithIndent(reference);
                changed = true;
            }

        var hasLibrary = project.Descendants().Any(e => e.Name.LocalName == "ProjectReference"
            && Path.GetFileName(((string?)e.Attribute("Include") ?? "").Replace('\\', '/')) == Path.GetFileName(library ?? "\0"));
        if (library != null && !hasLibrary)
        {
            var indent = ChildIndent(project);
            AddBeforeEnd(project, indent, new XElement("ItemGroup",
                new XText("\n" + indent + indent),
                new XElement("ProjectReference", new XAttribute("Include", library.Replace('/', '\\'))),
                new XText("\n" + indent)));
            changed = true;
        }

        foreach (var empty in project.Elements().Where(e => e.Name.LocalName == "ItemGroup" && !e.Elements().Any()).ToList())
            RemoveWithIndent(empty);
        return changed ? Serialize(doc, xml) : null;
    }

    static bool IsOliveReference(XElement e)
    {
        var include = (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update") ?? "";
        return e.Name.LocalName switch
        {
            "PackageReference" => Replacement.ReplacementLibrary.IsOliveAssembly(include),
            "ProjectReference" => Replacement.ReplacementLibrary.IsOliveAssembly(Path.GetFileNameWithoutExtension(include.Replace('\\', '/'))),
            _ => false,
        };
    }

    public static string? AddToSlnx(string xml, string projectPath)
    {
        if (xml.Contains($"\"{projectPath}\"", StringComparison.OrdinalIgnoreCase)) return null;
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        AddBeforeEnd(doc.Root!, ChildIndent(doc.Root!), new XElement("Project", new XAttribute("Path", projectPath)));
        return Serialize(doc, xml);
    }

    public static string? AddToSln(string text, string projectPath, Guid id)
    {
        var path = projectPath.Replace('/', '\\');
        if (text.Contains(path, StringComparison.OrdinalIgnoreCase)) return null;
        var newLine = FileText.NewLineOf(text);
        var global = text.IndexOf(newLine + "Global", StringComparison.Ordinal);
        if (global < 0) return null;
        var name = Path.GetFileNameWithoutExtension(path);
        var block = $"{newLine}Project(\"{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}\") = \"{name}\", \"{path}\", \"{{{id.ToString().ToUpperInvariant()}}}\"{newLine}EndProject";
        return text.Insert(global, block);
    }

    /// <summary>Drops solution entries for vendored Olive projects.</summary>
    public static string? RemoveOliveFromSlnx(string xml)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var olive = doc.Descendants().Where(e => e.Name.LocalName == "Project"
            && Replacement.ReplacementLibrary.IsOliveAssembly(Path.GetFileNameWithoutExtension(((string?)e.Attribute("Path") ?? "").Replace('\\', '/')))).ToList();
        if (olive.Count == 0) return null;
        olive.ForEach(RemoveWithIndent);
        return Serialize(doc, xml);
    }

    static bool IsMSharp(XElement reference)
    {
        var id = (string?)reference.Attribute("Include") ?? (string?)reference.Attribute("Update") ?? "";
        return id.Equals("MSharp", StringComparison.OrdinalIgnoreCase) || id.StartsWith("MSharp.", StringComparison.OrdinalIgnoreCase);
    }

    static bool HasTarget(XElement project, string name) =>
        project.Elements().Any(e => e.Name.LocalName == "Target" && (string?)e.Attribute("Name") == name);

    static XElement Target(string name, string condition, string command, string indent, string? inputs = null, string? outputs = null)
    {
        var target = new XElement("Target", new XAttribute("Name", name), new XAttribute("BeforeTargets", "BeforeBuild"), new XAttribute("Condition", condition));
        // Inputs/Outputs make MSBuild skip the target when every output is newer than every input.
        if (inputs != null) target.Add(new XAttribute("Inputs", inputs), new XAttribute("Outputs", outputs!));
        target.Add(
            new XText("\n" + indent + indent),
            new XElement("Exec", new XAttribute("Command", command), new XAttribute("WorkingDirectory", "$(MSBuildProjectDirectory)")),
            new XText("\n" + indent));
        return target;
    }

    internal static string ChildIndent(XElement parent) =>
        parent.Nodes().OfType<XText>().Select(t => t.Value).FirstOrDefault(v => v.Contains('\n'))?.Split('\n').Last() is { Length: > 0 } indent ? indent : "  ";

    internal static void AddBeforeEnd(XElement parent, string indent, XNode node)
    {
        // Keep the closing tag on its own line: insert before the trailing whitespace if there is one.
        if (parent.LastNode is XText trailing && string.IsNullOrWhiteSpace(trailing.Value))
            trailing.AddBeforeSelf(new XText("\n" + indent), node);
        else
            parent.Add(new XText("\n" + indent), node, new XText("\n"));
    }

    internal static void RemoveWithIndent(XElement element)
    {
        if (element.PreviousNode is XText text && string.IsNullOrWhiteSpace(text.Value)) text.Remove();
        element.Remove();
    }

    internal static string Serialize(XDocument doc, string original)
    {
        // Everything before the root element (declaration, comments, blank lines) is kept byte for byte.
        var rootName = "<" + doc.Root!.Name.LocalName;
        var rootStart = original.IndexOf(rootName, StringComparison.Ordinal);
        var prefix = rootStart > 0 ? original[..rootStart] : "";
        var newLine = FileText.NewLineOf(original);
        var body = doc.Root.ToString(SaveOptions.DisableFormatting).Replace("\r\n", "\n").Replace("\n", newLine);
        var text = prefix + body;
        return original.EndsWith('\n') && !text.EndsWith('\n') ? text + newLine : text;
    }

    [GeneratedRegex(@"^Project\(""\{[^}]+\}""\)\s*=\s*""[^""]*"",\s*""(?<path>[^""]+)"",\s*""(?<guid>\{[^}]+\})""")]
    private static partial Regex SlnProject();

    [GeneratedRegex(@"(?<from>^\s*FROM\s+(--platform=\S+\s+)?)(?<image>[^\s:@]+):(?<tag>\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DockerFrom();

    [GeneratedRegex(@"<TargetFrameworks?>\s*(net(coreapp)?)(?<version>\d+\.\d+)")]
    private static partial Regex TargetFramework();
}
