using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MSharpEject.Eject;

namespace MSharpEject.Tests;

static class FakeOlive
{
    // Signatures copied from Olive/-Extensions (String.cs, Linq.cs); bodies match Olive's behaviour.
    public const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        namespace Olive
        {
            public static partial class OliveExtensions
            {
                public static bool HasValue(this string text) => !string.IsNullOrEmpty(text);
                public static bool IsEmpty(this string text) => string.IsNullOrEmpty(text);
                public static string OrEmpty(this string text) => text ?? string.Empty;
                public static bool Lacks(this string text, string phrase, bool caseSensitive = false) => !text.Contains(phrase);
                public static bool IsAnyOf(this string text, params string[] items) => items.Contains(text);
                public static bool HasAny<TSource>(this IEnumerable<TSource> items) => items != null && items.Any();
                public static bool None<T>(this IEnumerable<T> items) => !items.HasAny();
                public static bool None<T>(this IEnumerable<T> items, Func<T, bool> criteria) => !items.Any(criteria);
                public static bool None<T>(this IEnumerable<T> items, Func<T, int, bool> criteria) => !items.Where(criteria).Any();
                public static bool Lacks<T>(this IEnumerable<T> items, T item) => !items.Contains(item);
                public static bool Lacks<T>(this IEnumerable<T> items, T? item) where T : struct => !items.Select(x => (T?)x).Contains(item);
                public static string ToString<T>(this IEnumerable<T> items, string separator) => items == null ? "{NULL}" : string.Join(separator, items);
            }
            public class GuidEntity { public Guid ID { get; set; } public bool IsNew => true; }
        }
        """;

    static readonly MetadataReference[] Framework =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Where(p => Path.GetFileName(p).StartsWith("System.") || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll")
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();

    public static readonly Lazy<byte[]> Image = new(() =>
    {
        var compilation = CSharpCompilation.Create("Olive", [CSharpSyntaxTree.ParseText(Source)], Framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        return stream.ToArray();
    });

    static readonly Lazy<MetadataReference> Reference = new(() => MetadataReference.CreateFromImage(Image.Value));

    public static Document Document(string source, string path = "Domain/Logic/Code.cs")
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Domain", "Domain", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: [.. Framework, Reference.Value]));
        return workspace.AddDocument(project.Id, Path.GetFileName(path), Microsoft.CodeAnalysis.Text.SourceText.From(source));
    }
}

static class FakeRepo
{
    public const string Slnx = """
        <Solution>
          <Project Path="Domain/Domain.csproj" />
          <Project Path="Olive/Olive.csproj" />
          <Project Path="M#/Model/#Model.csproj" />
        </Solution>
        """;

    /// <summary>A small M# solution that builds with the .NET 10 SDK alone; Olive is a local project.</summary>
    public static TempDir Create(bool withTests = false, bool generated = true)
    {
        var repo = new TempDir();
        repo.Write("Olive/Olive.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        repo.Write("Olive/OliveExtensions.cs", FakeOlive.Source);

        repo.Write("Domain/Domain.csproj", """
            <?xml version="1.0" encoding="utf-8"?>
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Olive\Olive.csproj" />
              </ItemGroup>
            </Project>
            """);
        repo.Write("Domain/Logic/Contact.cs", """
            using System.Collections.Generic;
            using Olive;

            namespace Domain
            {
                partial class Contact
                {
                    public bool Named => Name.HasValue();
                    public bool Unnamed => !Name.HasValue();
                    public static bool NoneNamed(IEnumerable<Contact> all) => all.None(c => c.Named);
                }
            }
            """);
        if (generated)
            repo.Write("Domain/[GEN-Entities]/Contact.cs", "namespace Domain { public partial class Contact : Olive.GuidEntity { public string Name { get; set; } } }");

        // Fails the build unless MSHARP_BUILD=FULL, the way M#'s generation step does when M# can't run.
        repo.Write("M#/Model/#Model.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <Target Name="Generate code" AfterTargets="AfterBuild">
                <Exec Condition="'$(MSHARP_BUILD)' != 'FULL'" Command="exit 1" />
              </Target>
            </Project>
            """);
        repo.Write("M#/Model/Domain/Contact.cs", "namespace Domain { public class ContactDefinition { } }");
        repo.Write(".gitignore", "bin/\nobj/\nDomain/\\[GEN-Entities\\]/\nM#/lib/*\n");
        repo.Write("Build.bat", "call msharp-build");

        var slnx = Slnx;
        if (withTests)
        {
            repo.Write("Tests/Tests.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsPackable>false</IsPackable></PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="MSTest" Version="3.11.1" />
                    <ProjectReference Include="..\Domain\Domain.csproj" />
                  </ItemGroup>
                </Project>
                """);
            repo.Write("Tests/ContactTests.cs", """
                using Microsoft.VisualStudio.TestTools.UnitTesting;
                namespace Tests
                {
                    [TestClass]
                    public class ContactTests
                    {
                        [TestMethod] public void Named() => Assert.IsTrue(new Domain.Contact { Name = "A" }.Named);
                        [TestMethod] public void Unnamed() => Assert.IsTrue(new Domain.Contact().Unnamed);
                    }
                }
                """);
            slnx = slnx.Replace("</Solution>", "  <Project Path=\"Tests/Tests.csproj\" />\n</Solution>");
        }
        repo.Write("Sample.slnx", slnx);
        return repo;
    }

    public static async Task CommitAllAsync(string repo)
    {
        using var log = TestLog.Create();
        foreach (var args in new[] { "init -q", "config user.email test@example.com", "config user.name Test", "config core.autocrlf false", "add -A", "commit -q -m init" })
        {
            var result = await ProcessRunner.RunAsync("git", args, repo, log);
            if (result.ExitCode != 0) throw new InvalidOperationException($"git {args}: {result.Error}");
        }
    }

    public static RunOptions NoVerify(bool dryRun = false) => new(DryRun: dryRun, AllowDirty: false, Verify: false);
}
