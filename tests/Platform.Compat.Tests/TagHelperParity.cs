using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace CompatTests;

public class TagHelperParity
{
    public class OliveThing : Olive.Entities.GuidEntity { }

    public class OurThing : Platform.Entities.GuidEntity { }

    // Razor passes bound attributes in the context only, the rest in both.
    static async Task<string> Render(TagHelper helper, string tag, (string Name, string Value)[] attributes, string content, params (string Name, string Value)[] bound)
    {
        var all = new TagHelperAttributeList(attributes.Concat(bound).Select(a => new TagHelperAttribute(a.Name, a.Value)));
        var context = new TagHelperContext(tag, all, new Dictionary<object, object>(), "id");
        var unbound = new TagHelperAttributeList(attributes.Select(a => new TagHelperAttribute(a.Name, a.Value)));
        var output = new TagHelperOutput(tag, unbound, (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent(content)));
        try { await helper.ProcessAsync(context, output); }
        catch (Exception e) { return "throws " + e.GetType().Name; }
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return helper.Order + " " + writer;
    }

    // Olive crashing isn't behaviour to copy, so only its output is compared.
    static async Task Same(Task<string> ours, Task<string> theirs)
    {
        var expected = await theirs;
        if (!expected.StartsWith("throws ")) await Assert.That(await ours).IsEqualTo(expected);
    }

    static readonly (string, string)[][] AttributeSets = [[("id", "Name"), ("name", "Name"), ("for", "Name"), ("class", "x")], [("class", "x"), ("name", "N")], [("for", "F"), ("id", "")]];

    public static IEnumerable<string> Values() => ["p_", "", null, "a.b"];

    [Test]
    [MethodDataSource(nameof(Values))]
    public async Task Prefix_ids_matches(string prefix)
    {
        foreach (var attributes in AttributeSets)
            await Same(Render(new Platform.Mvc.PrefixIdentificationsTagHelper { Prefix = prefix }, "input", attributes, "", ("asp-prefix", prefix)), Render(new Olive.Mvc.PrefixIdentificationsTagHelper { Prefix = prefix }, "input", attributes, "", ("asp-prefix", prefix)));
    }

    [Test]
    [MethodDataSource(nameof(Values))]
    public async Task Replace_this_matches(string replace)
    {
        const string content = "<input id=\"p_Name\" name=\"p_Name\" /><label for=\"p_Name\">p_</label>";
        foreach (var with in new[] { "q_", "", null })
            await Same(Render(new Platform.Mvc.ReplaceIdentificationsTagHelper { ReplaceThis = replace, WithThis = with }, "div", [("class", "c")], content, ("asp-replace-this", replace), ("asp-with-this", with)), Render(new Olive.Mvc.ReplaceIdentificationsTagHelper { ReplaceThis = replace, WithThis = with }, "div", [("class", "c")], content, ("asp-replace-this", replace), ("asp-with-this", with)));
    }

    static string Shape(Type helper) =>
        string.Join(" ; ", helper.GetCustomAttributes(typeof(HtmlTargetElementAttribute), false).Cast<HtmlTargetElementAttribute>().Select(a => $"{a.Tag}[{a.Attributes}] {a.TagStructure} {a.ParentTag}")) + " | " +
        string.Join(",", helper.GetProperties().Select(p => p.Name + "=" + ((HtmlAttributeNameAttribute)Attribute.GetCustomAttribute(p, typeof(HtmlAttributeNameAttribute)))?.Name).OrderBy(x => x));

    // Views pick helpers by these attribute names; a mismatch silently does nothing.
    [Test]
    public async Task Targets_match()
    {
        var olive = typeof(Olive.Mvc.Controller).Assembly.GetExportedTypes().Where(t => typeof(ITagHelper).IsAssignableFrom(t) && !t.IsAbstract).ToList();
        await Assert.That(olive.Count).IsGreaterThan(0);
        foreach (var theirs in olive)
        {
            var ours = typeof(Platform.Mvc.Controller).Assembly.GetType(theirs.FullName!.Replace("Olive.", "Platform."));
            await Assert.That(ours).IsNotNull();
            await Assert.That(Shape(ours!)).IsEqualTo(Shape(theirs));
        }
        await Assert.That(new Platform.Mvc.PrefixIdentificationsTagHelper().Order).IsEqualTo(new Olive.Mvc.PrefixIdentificationsTagHelper().Order);
        await Assert.That(new Platform.Mvc.ReplaceIdentificationsTagHelper().Order).IsEqualTo(new Olive.Mvc.ReplaceIdentificationsTagHelper().Order);
        await Assert.That(new Platform.Mvc.HiddenTagHelper().Order).IsEqualTo(new Olive.Mvc.HiddenTagHelper().Order);
    }

    [Test]
    public async Task Hidden_entity_id_matches()
    {
        var id = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        var theirs = new OliveThing { ID = id };
        var ours = new OurThing { ID = id };
        var metadata = new EmptyModelMetadataProvider();
        foreach (var type in new[] { "hidden", "text" })
        {
            (string, string)[] attributes = [("type", type), ("value", "old")];
            await Same(Render(new Platform.Mvc.HiddenTagHelper { For = new ModelExpression("Thing", metadata.GetModelExplorerForType(typeof(OurThing), ours)) }, "input", attributes, ""), Render(new Olive.Mvc.HiddenTagHelper { For = new ModelExpression("Thing", metadata.GetModelExplorerForType(typeof(OliveThing), theirs)) }, "input", attributes, ""));
        }
    }
}

