using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MSharpEject.Eject;

namespace MSharpEject.Replacement;

/// <summary>Moves a website off the Olive.MvcJs npm package onto the Platform front end, shipped as TypeScript the app's own build compiles.</summary>
public static class FrontEndSwap
{
    public const string Folder = "wwwroot/scripts/platform";

    public static IEnumerable<Change> Plan(string repo, string website)
    {
        string Rel(string f) => Path.GetRelativePath(repo, f).Replace('\\', '/');
        var package = Path.Combine(website, "package.json");
        if (!File.Exists(package) || !UsesOlive(File.ReadAllText(package))) yield break;

        foreach (var (path, text) in Sources())
            yield return new EditFile(Rel(Path.Combine(website, Folder, path)), "Front end replacing Olive.MvcJs", text, new UTF8Encoding(false));

        var (json, jsonEncoding) = FileText.Read(package);
        yield return new EditFile(Rel(package), "Drop the Olive.MvcJs package", PackageJson(json), jsonEncoding);

        foreach (var file in Audit.RepoScan.RepoFiles(website).Where(f => !IsLibrary(website, f)))
        {
            var transform = Transform(file);
            if (transform == null) continue;
            var (text, encoding) = FileText.Read(file);
            var edited = transform(text);
            if (edited != text) yield return new EditFile(Rel(file), "Point at the Platform front end", edited, encoding);
        }
    }

    public static bool UsesOlive(string packageJson) => Regex.IsMatch(packageJson, @"""olive\.mvc(js)?""\s*:", RegexOptions.IgnoreCase);

    static bool IsLibrary(string website, string file)
    {
        var path = Path.GetRelativePath(website, file).Replace('\\', '/');
        return path.StartsWith("wwwroot/lib/") || path.StartsWith(Folder + "/") || path.StartsWith("node_modules/");
    }

    static Func<string, string>? Transform(string file)
    {
        var name = Path.GetFileName(file);
        if (name.Equals("tsconfig.json", StringComparison.OrdinalIgnoreCase)) return TsConfig;
        if (name.Equals("references.js", StringComparison.OrdinalIgnoreCase)) return References;
        return Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".ts" => Script,
            ".cshtml" or ".css" or ".scss" => Markup,
            _ => null,
        };
    }

    // pako was only a dependency of Olive.MvcJs, the replacement uses it directly.
    public static string PackageJson(string text)
    {
        var line = Regex.Match(text, @"^([ \t]*)""olive\.mvc(js)?""\s*:\s*""[^""]*""(,?)[ \t]*\r?\n?", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!line.Success) return text;
        var pako = $"{line.Groups[1].Value}\"pako\": \"^2.1.0\"{line.Groups[3].Value}{(line.Value.EndsWith('\n') ? line.Value[^(line.Value.EndsWith("\r\n") ? 2 : 1)..] : "")}";
        return text.Remove(line.Index, line.Length).Insert(line.Index, Regex.IsMatch(text, @"""pako""\s*:") ? "" : pako);
    }

    public static string TsConfig(string text)
    {
        text = Regex.Replace(text, @"""olive/\*""\s*:\s*\[[^\]]*\]", $"\"platform/*\": [ \"{Folder}/*\" ]");
        return Regex.Replace(text, @"^[ \t]*""wwwroot/lib/olive\.mvc[^""]*""\s*,?[ \t]*\r?\n", "", RegexOptions.Multiline);
    }

    public static string References(string text)
    {
        text = Regex.Replace(text, @"(['""])olive\1\s*:\s*(['""])olive\.mvc/dist\2", "'platform': \"../scripts/platform\"");
        return Renames(text).Replace("IsOliveMvcLoaded", "IsPlatformLoaded").Replace("Olive scripts", "scripts");
    }

    public static string Script(string text) => Renames(text).Replace("isOliveMvcLoaded", "isPlatformLoaded");

    public static string Markup(string text) => text.Replace("olive-instant-search-item", "instant-search-item");

    static string Renames(string text) => Regex.Replace(text, @"(['""])olive/", "$1platform/").Replace("olivePage", "platformPage").Replace("OlivePage", "PlatformPage");

    public static List<(string Path, string Text)> Sources()
    {
        const string prefix = "Library/FrontEnd/";
        var assembly = typeof(FrontEndSwap).Assembly;
        return assembly.GetManifestResourceNames()
            .Select(n => (Resource: n, Path: n.Replace('\\', '/')))
            .Where(r => r.Path.StartsWith(prefix))
            .OrderBy(r => r.Path, StringComparer.Ordinal)
            .Select(r => (r.Path[prefix.Length..], Read(assembly, r.Resource)))
            .ToList();
    }

    static string Read(Assembly assembly, string resource)
    {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return reader.ReadToEnd();
    }
}
