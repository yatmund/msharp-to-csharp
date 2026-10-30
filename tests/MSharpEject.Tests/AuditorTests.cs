using MSharpEject.Audit;

namespace MSharpEject.Tests;

public class AuditorTests
{
    static TempDir FakeMSharpRepo()
    {
        var repo = new TempDir();
        repo.Write("Olive/Olive.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        repo.Write("Olive/Extensions.cs", """
            namespace Olive
            {
                public static class OliveExtensions { public static bool HasValue(this string s) => !string.IsNullOrEmpty(s); }
                public class GuidEntity { }
            }
            """);

        repo.Write("Domain/Domain.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="..\Olive\Olive.csproj" /></ItemGroup>
            </Project>
            """);
        repo.Write("Domain/[GEN-Entities]/Contact.cs", "namespace Domain { public partial class Contact : Olive.GuidEntity { public string Name { get; set; } } }");
        repo.Write("Domain/Logic/Contact.cs", "using Olive; namespace Domain { partial class Contact { public bool Named => Name.HasValue(); } }");
        repo.Write("Domain/Logic/Files.cs", "namespace Domain { static class Files { public const string Dir = \"App_Data\\\\Files\"; } }");

        repo.Write("M#/Model/#Model.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="MSharp" Version="6.1.0" /></ItemGroup>
              <Target Name="Generate code" AfterTargets="AfterBuild">
                <Exec Command="dotnet msharp.dsl.dll /build /model" />
              </Target>
            </Project>
            """);
        repo.Write("M#/Model/Domain/Contact.cs", "using MSharp; namespace Domain { public class Contact : EntityType { public Contact() { String(\"Name\"); } } }");

        repo.Write("Website/Dockerfile", "FROM mcr.microsoft.com/dotnet/aspnet:6.0-nanoserver-1809");
        repo.Write("Build.bat", "call msharp-build");
        return repo;
    }

    [Test]
    public async Task File_scans_without_semantic_pass()
    {
        using var repo = FakeMSharpRepo();

        var result = await Auditor.RunAsync(repo.Path, semantic: false, _ => { });

        await Assert.That(result.Packages.Select(p => p.Id)).IsEquivalentTo(["MSharp"]);
        await Assert.That(result.BuildSteps.Single().Text).IsEqualTo("dotnet msharp.dsl.dll /build /model");
        await Assert.That(result.Dsl["EntityType"]).IsEqualTo(1);
        await Assert.That(result.Generated.Single(g => g.Path == "Domain/[GEN-Entities]").Files).IsEqualTo(1);
        await Assert.That(result.Blockers.Select(b => b.Rule)).IsEquivalentTo(["windows-path", "windows-container", "geeks-reference", "windows-script"]);
        await Assert.That(result.Symbols.Count).IsEqualTo(0);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Semantic_pass_loads_projects_and_resolves_olive_symbols(CancellationToken _)
    {
        using var repo = FakeMSharpRepo();

        var result = await Auditor.RunAsync(repo.Path, semantic: true, _ => { });
        var uses = result.Symbols.ToDictionary(s => s.Symbol);

        await Assert.That(uses["OliveExtensions.HasValue"].Count).IsEqualTo(1);
        await Assert.That(uses["OliveExtensions.HasValue"].GeneratedCount).IsEqualTo(0);
        await Assert.That(uses["Olive.GuidEntity"].GeneratedCount).IsEqualTo(1);
        await Assert.That(result.Symbols.All(s => s.Assembly == "Olive")).IsTrue();
    }

    [Test]
    public async Task Markdown_report_has_every_section()
    {
        using var repo = FakeMSharpRepo();
        var result = await Auditor.RunAsync(repo.Path, semantic: false, _ => { });

        var md = Auditor.ToMarkdown(result);

        foreach (var heading in new[] { "## Geeks packages and tools", "## M# definitions", "## Generated output on disk",
                     "## Windows-only or Geeks build steps", "## Deployment blockers", "### windows-container (1)", "## Olive symbol usage" })
            await Assert.That(md).Contains(heading);
        await Assert.That(md).Contains("| M#/Model/#Model.csproj | MSharp | 6.1.0 |");
        await Assert.That(md).Contains("Skipped (`--no-semantic`).");
    }

    [Test]
    public async Task Markdown_warns_when_generated_output_missing()
    {
        using var repo = new TempDir();
        repo.Write("M#/Model/Domain/Contact.cs", "class Contact : EntityType { }");

        var md = Auditor.ToMarkdown(await Auditor.RunAsync(repo.Path, semantic: false, _ => { }));

        await Assert.That(md).Contains("No generated output. M# must run once before `eject`.");
    }
}
