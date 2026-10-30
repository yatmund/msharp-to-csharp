using MSharpEject.Audit;

namespace MSharpEject.Tests;

public class RepoScanTests
{
    [Test]
    [Arguments("Olive", true)]
    [Arguments("Olive.Entities.Data.SqlServer", true)]
    [Arguments("olive.mvc", true)]
    [Arguments("MSharp", true)]
    [Arguments("GCop.Common", true)]
    [Arguments("msharp-build", true)]
    [Arguments("OliveOil", false)]
    [Arguments("Newtonsoft.Json", false)]
    [Arguments("Hangfire", false)]
    public async Task IsGeeksPackage(string id, bool expected) =>
        await Assert.That(RepoScan.IsGeeksPackage(id)).IsEqualTo(expected);

    [Test]
    public async Task GeeksPackages_reads_include_update_and_child_version()
    {
        const string csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Olive" Version="2.1.340" />
                <PackageReference Update="Olive.Mvc"><Version>3.1.150</Version></PackageReference>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                <PackageReference Include="MSharp" />
              </ItemGroup>
            </Project>
            """;

        var packages = RepoScan.GeeksPackages("Domain/Domain.csproj", csproj).ToList();

        await Assert.That(packages.Count).IsEqualTo(3);
        await Assert.That(packages).Contains(new PackageRef("Domain/Domain.csproj", "Olive", "2.1.340"));
        await Assert.That(packages).Contains(new PackageRef("Domain/Domain.csproj", "Olive.Mvc", "3.1.150"));
        await Assert.That(packages).Contains(new PackageRef("Domain/Domain.csproj", "MSharp", null));
    }

    [Test]
    public async Task GeeksTools_reads_tool_manifest()
    {
        const string json = """
            { "version": 1, "tools": {
                "msharp-build": { "version": "1.2.3", "commands": ["msharp-build"] },
                "dotnet-ef": { "version": "10.0.0", "commands": ["dotnet-ef"] } } }
            """;

        var tools = RepoScan.GeeksTools(".config/dotnet-tools.json", json).ToList();

        await Assert.That(tools.Count).IsEqualTo(1);
        await Assert.That(tools[0]).IsEqualTo(new PackageRef(".config/dotnet-tools.json", "msharp-build", "1.2.3"));
    }

    [Test]
    public async Task GeeksNpmPackages_matches_name_or_github_spec()
    {
        const string json = """
            { "dependencies": { "olive.mvc": "Geeksltd/Olive.MvcJs#2.3.33", "jquery": "3.7.1" },
              "devDependencies": { "typescript": "5.0.0", "some-fork": "github:GeeksLtd/fork" } }
            """;

        var packages = RepoScan.GeeksNpmPackages("Website/package.json", json).Select(p => p.Id).ToList();

        await Assert.That(packages).IsEquivalentTo(["olive.mvc", "some-fork"]);
    }

    [Test]
    public async Task BuildSteps_flags_msharp_start_and_exe_tasks_only()
    {
        const string csproj = """
            <Project>
              <UsingTask AssemblyFile="wwwroot\Styles\build\SassCompiler.exe" TaskName="WebCompiler.CompilerBuildTask" />
              <UsingTask AssemblyFile="tools\Something.dll" TaskName="Other" />
              <Target Name="Generate code" AfterTargets="AfterBuild">
                <Exec WorkingDirectory="$(TargetDir)" Command="dotnet msharp.dsl.dll /build /ui" />
                <Exec Command="start /min &quot;&quot; msharp /diagnose" />
                <Exec Command="dotnet tool restore" />
              </Target>
            </Project>
            """;

        var findings = RepoScan.BuildSteps("Website/Website.csproj", csproj).ToList();

        await Assert.That(findings.Select(f => f.Rule)).IsEquivalentTo(["build-task-exe", "build-exec", "build-exec"]);
        await Assert.That(findings[0].Line).IsEqualTo(2);
    }

    [Test]
    public async Task DslInventory_counts_definitions_by_base_type()
    {
        string[] sources =
        [
            """
            using MSharp;
            namespace Domain
            {
                public class User : EntityType { public User() { Abstract(); } }
                public class Employee : SubType<User> { }
                public class Helper { }
            }
            """,
            """
            namespace Modules
            {
                public class ContactsList : MSharp.ListModule<Domain.Contact>
                {
                    public ContactsList() { ButtonColumn("c#:item.Name"); Column("c#:item.Phone"); HeaderText("Contacts"); }
                }
                public class ContactForm : FormModule<Domain.Contact> { }
            }
            namespace Contact { public class EnterPage : SubPage<ContactsPage> { } }
            """,
        ];

        var counts = RepoScan.DslInventory(sources);

        await Assert.That(counts["EntityType"]).IsEqualTo(1);
        await Assert.That(counts["SubType"]).IsEqualTo(1);
        await Assert.That(counts["ListModule"]).IsEqualTo(1);
        await Assert.That(counts["FormModule"]).IsEqualTo(1);
        await Assert.That(counts["SubPage"]).IsEqualTo(1);
        await Assert.That(counts["c# expressions"]).IsEqualTo(2);
        await Assert.That(counts.Count).IsEqualTo(6);
    }

    [Test]
    [Arguments("Logic/Image.cs", "using System.Drawing;", "windows-api")]
    [Arguments("Logic/Reg.cs", "var key = Microsoft.Win32.Registry.LocalMachine;", "windows-api")]
    [Arguments("Logic/Native.cs", "[DllImport(\"user32.dll\")]", "windows-api")]
    [Arguments("Logic/Paths.cs", "var p = \"C:\\\\Temp\\\\x.txt\";", "windows-path")]
    [Arguments("Logic/Paths.cs", "var p = \"App_Data\\\\Files\";", "windows-path")]
    [Arguments("Logic/Paths.cs", "var p = @\"App_Data\\Files\";", "windows-path")]
    [Arguments("Website/Dockerfile", "FROM mcr.microsoft.com/dotnet/aspnet:6.0-nanoserver-1809", "container-image")]
    [Arguments("Website/appsettings.json", "\"Url\": \"https://licensing.msharp.co.uk\"", "geeks-reference")]
    [Arguments("Website/Dockerfile", "FROM microsoft/aspnetcore:2.0 AS base", "container-image")]
    [Arguments("Domain/Domain.csproj", "<TargetFramework>net6.0</TargetFramework>", "eol-framework")]
    [Arguments("Domain/Domain.csproj", "<TargetFramework>netcoreapp3.1</TargetFramework>", "eol-framework")]
    [Arguments("Jenkinsfile", "git 'https://github.com/Geeksltd/MasterJenkinsfile'", "geeks-reference")]
    public async Task DeploymentBlockers_flags(string file, string line, string rule)
    {
        var findings = RepoScan.DeploymentBlockers(file, "// first\n" + line).ToList();

        await Assert.That(findings.Count).IsEqualTo(1);
        await Assert.That(findings[0].Rule).IsEqualTo(rule);
        await Assert.That(findings[0].Line).IsEqualTo(2);
    }

    [Test]
    [Arguments("Logic/Ok.cs", "var p = Path.Combine(\"App_Data\", \"Files\");")]
    [Arguments("Logic/Ok.cs", "var s = \"line\\nbreak\";")]
    [Arguments("Logic/Ok.cs", "using SkiaSharp;")]
    [Arguments("Website/Dockerfile", "FROM mcr.microsoft.com/dotnet/aspnet:10.0")]
    [Arguments("Domain/Domain.csproj", "<TargetFramework>net8.0</TargetFramework>")]
    [Arguments("Domain/Domain.csproj", "<TargetFrameworks>net10.0;net472</TargetFrameworks>")]
    [Arguments("Website/appsettings.json", "\"Url\": \"https://example.com\"")]
    [Arguments("README.md", "Built with msharp")]
    public async Task DeploymentBlockers_ignores(string file, string line) =>
        await Assert.That(RepoScan.DeploymentBlockers(file, line).Count()).IsEqualTo(0);

    [Test]
    public async Task DeploymentBlockers_flags_web_config_and_batch_files()
    {
        await Assert.That(RepoScan.DeploymentBlockers("Website/web.config", "<configuration />").Single().Rule).IsEqualTo("iis-config");
        await Assert.That(RepoScan.DeploymentBlockers("Build.bat", "@echo off").Single().Rule).IsEqualTo("windows-script");
    }

    [Test]
    [Arguments("Domain/[GEN-Entities]/Contact.cs", true)]
    [Arguments("Domain\\[GEN-DAL]\\ContactDataProvider.cs", true)]
    [Arguments("Website/Controllers/Modules/ContactsListController.cs", true)]
    [Arguments("Website/Views/Pages/Contacts.cshtml", true)]
    [Arguments("Website/app_Start/TaskManager.cs", true)]
    [Arguments("Website/Controllers/BaseController.cs", false)]
    [Arguments("Domain/Logic/Contact.cs", false)]
    public async Task IsGeneratedFile(string path, bool expected) =>
        await Assert.That(RepoScan.IsGeneratedFile(path)).IsEqualTo(expected);

    [Test]
    public async Task RepoFiles_skips_build_output_and_dependencies()
    {
        using var repo = new TempDir();
        repo.Write("Domain/Logic/A.cs", "");
        repo.Write("Domain/bin/Debug/A.dll", "");
        repo.Write("Website/obj/x.cs", "");
        repo.Write("Website/node_modules/pkg/index.js", "");
        repo.Write("Website/wwwroot/lib/jquery.js", "");

        var files = RepoScan.RepoFiles(repo.Path).Select(f => Path.GetRelativePath(repo.Path, f).Replace('\\', '/')).ToList();

        await Assert.That(files).IsEquivalentTo(["Domain/Logic/A.cs"]);
    }

    [Test]
    public async Task ScanGeneratedFolder_reports_missing_and_present()
    {
        using var repo = new TempDir();
        repo.Write("Domain/[GEN-Entities]/Contact.cs", "");
        repo.Write("Domain/[GEN-Entities]/User.cs", "");
        repo.Write("DB/Tables/-Warning!.txt", "");

        var present = RepoScan.ScanGeneratedFolder(repo.Path, "Domain/[GEN-Entities]");
        var missing = RepoScan.ScanGeneratedFolder(repo.Path, "Domain/[GEN-DAL]");
        var warningOnly = RepoScan.ScanGeneratedFolder(repo.Path, "DB/Tables");
        await Assert.That(warningOnly.Files).IsEqualTo(0);

        await Assert.That(present.Files).IsEqualTo(2);
        await Assert.That(present.Newest).IsNotNull();
        await Assert.That(missing.Files).IsEqualTo(0);
        await Assert.That(missing.Newest).IsNull();
    }
}
