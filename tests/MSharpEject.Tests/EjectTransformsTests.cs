using MSharpEject.Eject;

namespace MSharpEject.Tests;

public class EjectTransformsTests
{
    [Test]
    public async Task Gitignore_drops_generated_and_msharp_lines_only()
    {
        const string text = "bin/\r\nDomain/\\[GEN-DAL\\]/\r\nDomain/\\[GEN-Entities\\]/\r\n# Website/Views/Pages/ comment\r\nWebsite/Views/Pages/\r\nWebsite/Controllers/Modules/\r\nWebsite/app_start/TaskManager.cs\r\nM#/lib/*\r\n!M#/lib/netcoreapp*/\r\nWebsite/wwwroot/lib/\r\n";

        var result = EjectTransforms.Gitignore(text);

        await Assert.That(result).IsEqualTo("bin/\r\n# Website/Views/Pages/ comment\r\nWebsite/wwwroot/lib/\r\n");
    }

    [Test]
    public async Task Gitignore_unchanged_returns_null() =>
        await Assert.That(EjectTransforms.Gitignore("bin/\nobj/\n")).IsNull();

    [Test]
    public async Task Sln_removes_dsl_projects_and_their_configuration_lines()
    {
        const string sln = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Domain", "Domain\Domain.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "#Model", "M#\Model\#Model.csproj", "{90A0E952-66A9-49A7-96E7-89F63BD4F4E7}"
            EndProject
            Global
            	GlobalSection(ProjectConfigurationPlatforms) = postSolution
            		{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU
            		{90A0E952-66A9-49A7-96E7-89F63BD4F4E7}.Debug|Any CPU.Build.0 = Debug|Any CPU
            	EndGlobalSection
            EndGlobal
            """;

        var result = EjectTransforms.Sln(sln)!;

        await Assert.That(result).DoesNotContain("#Model");
        await Assert.That(result).DoesNotContain("90A0E952");
        await Assert.That(result).Contains("Domain\\Domain.csproj");
        await Assert.That(result).Contains("{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0");
        await Assert.That(EjectTransforms.Sln(result)).IsNull();
    }

    [Test]
    public async Task Slnx_removes_dsl_projects()
    {
        var result = EjectTransforms.Slnx(FakeRepo.Slnx)!;

        await Assert.That(result).DoesNotContain("M#");
        await Assert.That(result).Contains("<Project Path=\"Domain/Domain.csproj\" />");
        await Assert.That(EjectTransforms.Slnx(result)).IsNull();
    }

    [Test]
    public async Task ToolsManifest_removes_geeks_tools()
    {
        const string json = """{ "version": 1, "isRoot": true, "tools": { "msharp-build": { "version": "1.0.0", "commands": ["msharp-build"] }, "dotnet-ef": { "version": "10.0.0", "commands": ["dotnet-ef"] } } }""";

        var result = EjectTransforms.ToolsManifest(json)!;

        await Assert.That(result).DoesNotContain("msharp-build");
        await Assert.That(result).Contains("dotnet-ef");
        await Assert.That(EjectTransforms.ToolsManifest(result)).IsNull();
    }

    [Test]
    public async Task SassPairs_reads_compilerconfig()
    {
        const string json = """[ { "outputFile": "wwwroot/styles/Blank.css", "inputFile": "wwwroot/styles/Blank.scss" }, { "outputFile": "a.min.js", "inputFile": "a.js" } ]""";

        await Assert.That(EjectTransforms.SassPairs(json)).IsEquivalentTo([new SassPair("wwwroot/styles/Blank.scss", "wwwroot/styles/Blank.css")]);
    }

    const string WebsiteCsproj = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Project Sdk=\"Microsoft.NET.Sdk.Web\">\r\n  <ItemGroup>\r\n    <PackageReference Include=\"MSharp\" Version=\"5.2.308\" />\r\n  </ItemGroup>\r\n  <ItemGroup>\r\n    <PackageReference Include=\"Olive\" Version=\"2.1.340\" />\r\n  </ItemGroup>\r\n  <UsingTask AssemblyFile=\"wwwroot\\Styles\\build\\SassCompiler.exe\" TaskName=\"WebCompiler.CompilerBuildTask\" />\r\n  <Target Name=\"WebCompile\" AfterTargets=\"PostBuildEvent\">\r\n    <WebCompiler.CompilerBuildTask FileName=\"compilerconfig.json\" />\r\n  </Target>\r\n</Project>\r\n";

    static readonly FrontEnd Web = new(HasPackageJson: true, UsesYarn: true, HasTsConfig: true, Sass: [new("wwwroot/styles/a.scss", "wwwroot/styles/a.css")]);

    [Test]
    public async Task Csproj_swaps_msharp_and_webcompiler_for_cross_platform_targets()
    {
        var result = EjectTransforms.Csproj(WebsiteCsproj, Web)!;

        await Assert.That(result).StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Project Sdk=\"Microsoft.NET.Sdk.Web\">\r\n");
        await Assert.That(result).DoesNotContain("MSharp");
        await Assert.That(result).DoesNotContain("WebCompiler");
        await Assert.That(result).Contains("<PackageReference Include=\"Olive\" Version=\"2.1.340\" />");
        await Assert.That(result).Contains($"npx --yes yarn@{EjectTransforms.YarnVersion} install");
        await Assert.That(result).Contains($"npx --yes sass@{EjectTransforms.SassVersion} --no-source-map --quiet wwwroot/styles/a.scss:wwwroot/styles/a.css");
        await Assert.That(result).Contains("Outputs=\"wwwroot/styles/a.css\"");
        await Assert.That(result).Contains($"<PackageReference Include=\"Microsoft.TypeScript.MSBuild\" Version=\"{EjectTransforms.TypeScriptMSBuildVersion}\" />");
        await Assert.That(result).DoesNotContain("\r\r");
        await Assert.That(result.Replace("\r\n", "")).DoesNotContain("\n");
        await Assert.That(System.Xml.Linq.XDocument.Parse(result).Root!.Name.LocalName).IsEqualTo("Project");
    }

    [Test]
    public async Task Csproj_is_idempotent()
    {
        var once = EjectTransforms.Csproj(WebsiteCsproj, Web)!;

        await Assert.That(EjectTransforms.Csproj(once, Web)).IsNull();
    }

    [Test]
    public async Task Csproj_uses_npm_without_yarn_and_skips_missing_parts()
    {
        var result = EjectTransforms.Csproj(WebsiteCsproj, new FrontEnd(true, false, false, []))!;

        await Assert.That(result).Contains("Command=\"npm install\"");
        await Assert.That(result).DoesNotContain("CompileSass");
        await Assert.That(result).DoesNotContain("TypeScript");
    }

    [Test]
    public async Task Csproj_without_msharp_or_front_end_is_unchanged() =>
        await Assert.That(EjectTransforms.Csproj("<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <PackageReference Include=\"Olive\" Version=\"1\" />\n  </ItemGroup>\n</Project>\n", null)).IsNull();

    [Test]
    public async Task Csproj_drops_item_group_left_empty()
    {
        const string xml = "<Project>\n  <ItemGroup>\n    <PackageReference Include=\"MSharp\" Version=\"1\" />\n  </ItemGroup>\n</Project>\n";

        await Assert.That(EjectTransforms.Csproj(xml, null)).IsEqualTo("<Project>\n</Project>\n");
    }

    [Test]
    [Arguments("FROM microsoft/aspnetcore:2.0 AS base", "FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base")]
    [Arguments("FROM microsoft/aspnetcore-build:2.0 AS build", "FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build")]
    [Arguments("FROM mcr.microsoft.com/dotnet/aspnet:6.0-nanoserver-1809", "FROM mcr.microsoft.com/dotnet/aspnet:6.0")]
    [Arguments("FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build", "FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build")]
    [Arguments("FROM microsoft/dotnet:2.1-aspnetcore-runtime", "FROM mcr.microsoft.com/dotnet/aspnet:6.0")]
    public async Task Dockerfile_moves_to_current_linux_images(string before, string after) =>
        await Assert.That(EjectTransforms.Dockerfile($"{before}\nWORKDIR /app\n", "6.0")).IsEqualTo($"{after}\nWORKDIR /app\n");

    [Test]
    [Arguments("FROM mcr.microsoft.com/dotnet/aspnet:8.0")]
    [Arguments("FROM node:20 AS frontend")]
    [Arguments("FROM mcr.microsoft.com/mssql/server:2022-latest")]
    public async Task Dockerfile_leaves_current_and_unrelated_images(string line) =>
        await Assert.That(EjectTransforms.Dockerfile(line + "\n", "6.0")).IsNull();

    [Test]
    [Arguments("<TargetFramework>net6.0</TargetFramework>", "6.0")]
    [Arguments("<TargetFrameworks>net8.0;net10.0</TargetFrameworks>", "8.0")]
    [Arguments("<TargetFramework>netcoreapp3.1</TargetFramework>", "3.1")]
    public async Task DotnetVersion_reads_target_framework(string xml, string expected) =>
        await Assert.That(EjectTransforms.DotnetVersion($"<Project><PropertyGroup>{xml}</PropertyGroup></Project>")).IsEqualTo(expected);
}
