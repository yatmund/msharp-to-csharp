using MSharpEject.DeOlive;

namespace MSharpEject.Tests;

public class DeOliveTests
{
    static async Task<string> Rewrite(string body, string usings = "using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing Olive;\n")
    {
        var source = usings + "class C\n{\n" + body + "\n}\n";
        var rewrite = await DeOliver.RewriteAsync(FakeOlive.Document(source), CancellationToken.None);
        return rewrite!.Applied.Count == 0 ? source : rewrite.NewText;
    }

    static string Member(string rewritten) => rewritten.Split("class C\n{\n")[1].Split("\n}")[0];

    [Test]
    [Arguments("bool M(string s) => s.HasValue();", "bool M(string s) => !string.IsNullOrEmpty(s);")]
    [Arguments("bool M(string s) => !s.HasValue();", "bool M(string s) => string.IsNullOrEmpty(s);")]
    [Arguments("bool M(string s) => s.IsEmpty();", "bool M(string s) => string.IsNullOrEmpty(s);")]
    [Arguments("bool M(string s) => !s.IsEmpty();", "bool M(string s) => !string.IsNullOrEmpty(s);")]
    [Arguments("int M(string s) => s.OrEmpty().Length;", "int M(string s) => (s ?? string.Empty).Length;")]
    [Arguments("string M(string s) => s.OrEmpty();", "string M(string s) => s ?? string.Empty;")]
    [Arguments("bool M(int[] a) => a.HasAny();", "bool M(int[] a) => a?.Any() == true;")]
    [Arguments("bool M(int[] a) => a.None();", "bool M(int[] a) => a?.Any() != true;")]
    [Arguments("bool M(int[] a) => !a.None();", "bool M(int[] a) => a?.Any() == true;")]
    [Arguments("bool M(int[] a) => a.None(x => x > 1);", "bool M(int[] a) => !a.Any(x => x > 1);")]
    [Arguments("bool M(List<string> a) => a.Lacks(\"x\");", "bool M(List<string> a) => !a.Contains(\"x\");")]
    [Arguments("bool M(string s) => s.OrEmpty().HasValue();", "bool M(string s) => !string.IsNullOrEmpty(s ?? string.Empty);")]
    [Arguments("bool M(int?[] a) => a.Lacks(3);", "bool M(int?[] a) => !a.Contains(3);")]
    [Arguments("bool M(List<int> a) => (a.Count > 0 ? a : null).HasAny();", "bool M(List<int> a) => (a.Count > 0 ? a : null)?.Any() == true;")]
    [Arguments("bool M(string a, string b) => (a + b).HasValue() && a.HasValue();", "bool M(string a, string b) => !string.IsNullOrEmpty(a + b) && !string.IsNullOrEmpty(a);")]
    public async Task Rewrites_to_exact_bcl_equivalent(string before, string after) =>
        await Assert.That(Member(await Rewrite(before))).IsEqualTo(after);

    [Test]
    [Arguments("bool M(string s) => s.Lacks(\"x\");")]
    [Arguments("bool M(string s) => s.IsAnyOf(\"a\", \"b\");")]
    [Arguments("bool M(int[] a) => a.None((x, i) => x > i);")]
    [Arguments("bool M(int[] a, int? x) => a.Lacks(x);")]
    [Arguments("string M(int[] a) => a.ToString(\", \");")]
    [Arguments("bool M(C c) => c?.Name.HasValue() == true; string Name = \"\";")]
    public async Task Leaves_calls_without_an_exact_equivalent(string body) =>
        await Assert.That(Member(await Rewrite(body))).IsEqualTo(body);

    [Test]
    public async Task Leaves_calls_inside_expression_trees_for_olive_sql_translation()
    {
        const string body = "System.Linq.Expressions.Expression<Func<string, bool>> Query = s => s.HasValue();\nFunc<string, bool> Delegate = s => s.HasValue();";

        var rewrite = await DeOliver.RewriteAsync(FakeOlive.Document("using System;\nusing Olive;\nclass C\n{\n" + body + "\n}\n"), CancellationToken.None);

        await Assert.That(rewrite!.SkippedInQueries).IsEqualTo(1);
        await Assert.That(rewrite.NewText).Contains("Query = s => s.HasValue();");
        await Assert.That(rewrite.NewText).Contains("Delegate = s => !string.IsNullOrEmpty(s);");
    }

    [Test]
    public async Task Does_not_touch_same_named_methods_from_elsewhere()
    {
        const string source = "static class Mine { public static bool HasValue(this string s) => s != null; }\nclass C { bool M(string s) => s.HasValue(); }\n";

        var rewrite = await DeOliver.RewriteAsync(FakeOlive.Document(source), CancellationToken.None);

        await Assert.That(rewrite!.Applied).IsEmpty();
    }

    [Test]
    public async Task Removes_using_olive_when_nothing_else_needs_it_and_adds_linq()
    {
        var result = await Rewrite("bool M(string s, int[] a) => s.HasValue() && a.None();", "using Olive;\n");

        await Assert.That(result).StartsWith("using System.Linq;\nclass C");
        await Assert.That(result).DoesNotContain("using Olive;");
    }

    [Test]
    public async Task Keeps_using_olive_while_other_olive_calls_remain()
    {
        var result = await Rewrite("bool M(string s) => s.HasValue();\nstring N(int[] a) => a.ToString(\", \");", "using Olive;\n");

        await Assert.That(result).Contains("using Olive;");
        await Assert.That(result).Contains("!string.IsNullOrEmpty(s)");
    }

    [Test]
    public async Task Rewritten_code_has_the_same_behaviour_including_nulls()
    {
        // Compiles original and rewritten bodies against the fake Olive and compares results on edge-case inputs.
        string[] inputs = ["null", "\"\"", "\"x\""];
        foreach (var input in inputs)
            foreach (var call in new[] { "s.HasValue()", "!s.HasValue()", "s.IsEmpty()", "s.OrEmpty() == \"\"" })
            {
                var before = await Evaluate($"string s = {input}; return {call};", rewrite: false);
                var after = await Evaluate($"string s = {input}; return {call};", rewrite: true);
                await Assert.That(after).IsEqualTo(before);
            }

        foreach (var input in new[] { "null", "new int[0]", "new[] { 1, 2 }" })
            foreach (var call in new[] { "a.HasAny()", "a.None()", "!a.None()" })
            {
                var before = await Evaluate($"int[] a = {input}; return {call};", rewrite: false);
                var after = await Evaluate($"int[] a = {input}; return {call};", rewrite: true);
                await Assert.That(after).IsEqualTo(before);
            }
    }

    static async Task<bool> Evaluate(string body, bool rewrite)
    {
        var source = "using System;\nusing System.Linq;\nusing Olive;\npublic static class Probe\n{\n    public static bool Run()\n    {\n        " + body + "\n    }\n}\n";
        var document = FakeOlive.Document(source);
        if (rewrite && await DeOliver.RewriteAsync(document, CancellationToken.None) is { Applied.Count: > 0 } r)
            document = document.WithText(Microsoft.CodeAnalysis.Text.SourceText.From(r.NewText));

        var compilation = (await document.Project.GetCompilationAsync())!
            .WithOptions(new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using var probe = new MemoryStream();
        var emitted = compilation.Emit(probe);
        if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics));

        var context = new System.Runtime.Loader.AssemblyLoadContext("probe", isCollectible: true);
        try
        {
            context.Resolving += (ctx, name) => name.Name == "Olive" ? ctx.LoadFromStream(new MemoryStream(FakeOlive.Image.Value)) : null;
            var assembly = context.LoadFromStream(new MemoryStream(probe.ToArray()));
            return (bool)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }
}
