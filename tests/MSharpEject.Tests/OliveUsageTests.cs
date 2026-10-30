using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MSharpEject.Audit;

namespace MSharpEject.Tests;

public class OliveUsageTests
{
    const string FakeOlive = """
        namespace Olive
        {
            public static class OliveExtensions
            {
                public static bool HasValue(this string text) => !string.IsNullOrEmpty(text);
                public static bool None<T>(this System.Collections.Generic.IEnumerable<T> items) => true;
            }
            public class GuidEntity { public System.Guid ID { get; set; } public bool IsNew => true; }
            public class LogEventsAttribute : System.Attribute { public LogEventsAttribute(bool on) { } }
            public interface IDatabase { T Get<T>(System.Guid id); }
        }
        """;

    static readonly MetadataReference[] Framework =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();

    static MetadataReference Compile(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(assemblyName, [CSharpSyntaxTree.ParseText(source)], Framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    static Compilation App(params (string Path, string Source)[] files) =>
        CSharpCompilation.Create("Domain",
            files.Select(f => CSharpSyntaxTree.ParseText(f.Source, path: f.Path)),
            [.. Framework, Compile("Olive", FakeOlive)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    [Test]
    public async Task Counts_extensions_types_attributes_and_members()
    {
        var app = App(("Domain/Logic/Contact.cs", """
            using Olive;
            namespace Domain
            {
                [LogEvents(false)]
                public partial class Contact : GuidEntity
                {
                    public string Name { get; set; }
                    public bool Valid() => Name.HasValue() && !IsNew && "x".HasValue();
                }
            }
            """));

        var uses = OliveUsage.Collect([app], _ => false).ToDictionary(u => u.Symbol);

        await Assert.That(uses["OliveExtensions.HasValue"].Count).IsEqualTo(2);
        await Assert.That(uses["OliveExtensions.HasValue"].Kind).IsEqualTo("extension");
        await Assert.That(uses["Olive.GuidEntity"].Kind).IsEqualTo("class");
        await Assert.That(uses["Olive.LogEventsAttribute"].Kind).IsEqualTo("attribute");
        await Assert.That(uses["GuidEntity.IsNew"].Kind).IsEqualTo("property");
        await Assert.That(uses.Values.All(u => u.Assembly == "Olive")).IsTrue();
    }

    [Test]
    public async Task Generic_uses_collapse_to_one_definition()
    {
        var app = App(("A.cs", """
            using Olive;
            class A
            {
                bool X(int[] a, string[] b, IDatabase db) => a.None() && b.None() && db.Get<string>(default) != db.Get<int>(default).ToString();
            }
            """));

        var uses = OliveUsage.Collect([app], _ => false).ToDictionary(u => u.Symbol);

        await Assert.That(uses["OliveExtensions.None"].Count).IsEqualTo(2);
        await Assert.That(uses["IDatabase.Get"].Count).IsEqualTo(2);
    }

    [Test]
    public async Task Ignores_symbols_declared_in_source_and_other_assemblies()
    {
        var app = App(("A.cs", """
            using System.Linq;
            static class Mine { public static bool HasValue(this string s) => s != null; }
            class A { bool X(string s, int[] a) => s.HasValue() && a.Any(); }
            """));

        await Assert.That(OliveUsage.Collect([app], _ => false).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Splits_generated_from_hand_written_and_counts_files()
    {
        var app = App(
            ("Domain/[GEN-Entities]/Contact.cs", "using Olive; namespace Domain { public partial class Contact : GuidEntity { } }"),
            ("Domain/Logic/Contact.cs", "using Olive; namespace Domain { partial class Contact { bool X(string s) => s.HasValue(); } }"),
            ("Domain/Logic/User.cs", "namespace Domain { class User : Olive.GuidEntity { } }"));

        var uses = OliveUsage.Collect([app], f => RepoScan.IsGeneratedFile(f)).ToDictionary(u => u.Symbol);

        await Assert.That(uses["Olive.GuidEntity"].Count).IsEqualTo(2);
        await Assert.That(uses["Olive.GuidEntity"].GeneratedCount).IsEqualTo(1);
        await Assert.That(uses["Olive.GuidEntity"].Files).IsEqualTo(2);
        await Assert.That(uses["OliveExtensions.HasValue"].GeneratedCount).IsEqualTo(0);
    }

    [Test]
    public async Task Still_counts_when_code_does_not_compile()
    {
        // Hand-written partials reference generated members that aren't on disk yet.
        var app = App(("A.cs", """
            using Olive;
            class A { bool X() => MissingGeneratedProperty.HasValue(); bool Y(Undeclared u) => "x".HasValue(u); }
            """));

        var uses = OliveUsage.Collect([app], _ => false).ToDictionary(u => u.Symbol);

        await Assert.That(uses["OliveExtensions.HasValue"].Count).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Sorted_by_count_descending()
    {
        var app = App(("A.cs", """
            using Olive;
            class A : GuidEntity { bool X(string s) => s.HasValue() && s.HasValue() && s.HasValue(); }
            """));

        var uses = OliveUsage.Collect([app], _ => false);

        await Assert.That(uses[0].Symbol).IsEqualTo("OliveExtensions.HasValue");
    }
}
