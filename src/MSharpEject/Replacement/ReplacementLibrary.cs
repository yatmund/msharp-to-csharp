using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace MSharpEject.Replacement;

/// <param name="Assembly">The Olive assembly this module stands in for.</param>
/// <param name="Packages">NuGet packages the module needs once Olive's own dependencies are gone, as "Id/Version".</param>
/// <param name="NeedsReferences">Assemblies the repo must already reference for the in-memory compile, by file name without extension.</param>
public record LibraryModule(string Name, string Assembly, string[] Packages, bool Web = false, string[] NeedsReferences = null);

/// <summary>The in-repo replacement for Olive. Sources ship embedded in the tool, one folder per module.</summary>
public static partial class ReplacementLibrary
{
    public const string Root = "Platform";
    public const string ProjectPath = "Platform/Platform.csproj";

    /// <summary>Olive type names that would keep the word Olive in the code.</summary>
    public static readonly Dictionary<string, string> RenamedTypes = new() { ["OliveExtensions"] = "Extensions", ["OliveEntitiesExtensions"] = "EntitiesExtensions", ["OliveWebExtensions"] = "WebExtensions", ["OliveSecurityExtensions"] = "SecurityExtensions", ["OliveMvcExtensions"] = "MvcExtensions" };

    public static readonly LibraryModule[] Modules =
    [
        new("Core", "Olive", []),
        new("Runtime", "Olive", ["Microsoft.Extensions.Configuration/8.0.0", "Microsoft.Extensions.Configuration.Binder/8.0.2",
            "Microsoft.Extensions.DependencyInjection.Abstractions/8.0.2", "Microsoft.Extensions.Logging/8.0.1"],
            NeedsReferences: ["Microsoft.Extensions.Configuration.Abstractions", "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Logging.Abstractions"]),
        new("Entities", "Olive.Entities", []),
        new("Audit", "Olive.Audit", []),
        new("Data", "Olive.Entities.Data", []),
        new("SqlServer", "Olive.Entities.Data.SqlServer", ["System.Data.SqlClient/4.8.6"]),
        new("Csv", "Olive.CSV", ["CsvHelper/30.0.1"]),
        new("Export", "Olive.Export", []),
        new("ApiClient", "Olive.ApiClient", ["Newtonsoft.Json/13.0.3", "Microsoft.Extensions.Http/8.0.1"]),
        new("Microservices", "Olive.Microservices", []),
        new("Web", "Olive.Web", [], Web: true),
        new("Security", "Olive.Mvc.Security", ["System.IdentityModel.Tokens.Jwt/8.3.0"], Web: true),
        new("Mvc", "Olive.Mvc", ["Newtonsoft.Json/13.0.3"], Web: true),
        new("Email", "Olive.Email", ["Newtonsoft.Json/13.0.3"]),
    ];

    static readonly Dictionary<string, string[]> Requires = new()
    {
        ["Runtime"] = ["Core"],
        ["Entities"] = ["Runtime"],
        ["Audit"] = ["Entities"],
        ["Data"] = ["Entities", "Audit"],
        ["SqlServer"] = ["Data"],
        ["Csv"] = ["Entities"],
        ["Export"] = ["Data"],
        ["ApiClient"] = ["Runtime"],
        ["Microservices"] = ["ApiClient"],
        ["Web"] = ["Entities", "ApiClient"],
        ["Security"] = ["Runtime"],
        ["Mvc"] = ["Web", "Security", "Data"],
        ["Email"] = ["Data"],
    };

    public static bool IsOliveAssembly(string? name) =>
        name != null && (name.Equals("Olive", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Olive.", StringComparison.OrdinalIgnoreCase));

    public static List<LibraryModule> For(IEnumerable<string> oliveAssemblies)
    {
        var names = oliveAssemblies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wanted = Modules.Where(m => names.Contains(m.Assembly)).Select(m => m.Name).ToHashSet();
        for (var added = true; added;)
            added = wanted.ToList().SelectMany(n => Requires.GetValueOrDefault(n) ?? []).Aggregate(false, (any, r) => wanted.Add(r) | any);
        return Modules.Where(m => wanted.Contains(m.Name)).ToList();
    }

    /// <summary>Where an Olive symbol lives in the library, as a documentation comment id.</summary>
    public static string MapId(string oliveId)
    {
        foreach (var (from, to) in RenamedTypes) oliveId = Regex.Replace(oliveId, $@"(?<![\w])Olive\.{from}(?![\w])", $"{Root}.{to}");
        return OliveRoot().Replace(oliveId, Root + ".");
    }

    public static string? MappedId(ISymbol symbol) =>
        DocumentationCommentId.CreateDeclarationId(symbol) is { } id ? MapId(id) : null;

    public static List<(string Path, string Text)> Sources(IEnumerable<LibraryModule> modules)
    {
        var assembly = typeof(ReplacementLibrary).Assembly;
        var wanted = modules.Select(m => $"Library/{m.Name}/").ToList();
        return assembly.GetManifestResourceNames()
            .Select(n => (Resource: n, Path: n.Replace('\\', '/')))
            .Where(r => wanted.Any(r.Path.StartsWith))
            .OrderBy(r => r.Path, StringComparer.Ordinal)
            .Select(r => ($"{Root}/{r.Path["Library/".Length..]}", Read(assembly, r.Resource)))
            .ToList();
    }

    static string Read(Assembly assembly, string resource)
    {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return reader.ReadToEnd();
    }

    public static string Csproj(string targetFramework, IReadOnlyList<LibraryModule> modules)
    {
        var lines = new List<string>
        {
            "<Project Sdk=\"Microsoft.NET.Sdk\">",
            "",
            "  <PropertyGroup>",
            $"    <TargetFramework>{targetFramework}</TargetFramework>",
            $"    <RootNamespace>{Root}</RootNamespace>",
            "    <Nullable>disable</Nullable>",
            "    <ImplicitUsings>disable</ImplicitUsings>",
            "    <LangVersion>latest</LangVersion>",
            "  </PropertyGroup>",
        };
        var packages = modules.SelectMany(m => m.Packages).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (modules.Any(m => m.Web) || packages.Count > 0)
        {
            lines.Add("");
            lines.Add("  <ItemGroup>");
            if (modules.Any(m => m.Web)) lines.Add("    <FrameworkReference Include=\"Microsoft.AspNetCore.App\" />");
            foreach (var p in packages)
                lines.Add($"    <PackageReference Include=\"{p.Split('/')[0]}\" Version=\"{p.Split('/')[1]}\" />");
            lines.Add("  </ItemGroup>");
        }
        lines.Add("");
        lines.Add("</Project>");
        return string.Join("\n", lines) + "\n";
    }

    [GeneratedRegex(@"(?<![\w.])Olive\.")]
    private static partial Regex OliveRoot();
}
