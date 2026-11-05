using System.Linq.Expressions;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CompatTests;

/// <summary>The front end reads this markup by class and name, so it must match Olive's exactly.</summary>
public class FileUploadMarkupParity
{
    public class FakeHtml : DispatchProxy
    {
        protected override object Invoke(MethodInfo m, object[] a) =>
            m.ReturnType == typeof(IHtmlContent) ? new HtmlString("<<" + m.Name + "(" + string.Join(",", a.Select(x => x is string s ? s : Newtonsoft.Json.JsonConvert.SerializeObject(x))) + ")>>")
            : m.ReturnType.IsValueType ? Activator.CreateInstance(m.ReturnType) : null;
    }

    public class TheirModel { public Olive.Mvc.BlobViewModel Photo { get; set; } }
    public class OurModel { public Platform.Mvc.BlobViewModel Photo { get; set; } }

    static string Text(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return Regex.Replace(writer.ToString(), @"\s+", " ").Trim();
    }

    public static IEnumerable<(string File, string Url)> Blobs() => [(null, null), ("a.pdf", "/f/a.pdf"), ("a&b.pdf", "/f/x")];

    [Test]
    [MethodDataSource(nameof(Blobs))]
    public async Task Markup_matches(string file, string url)
    {
        _ = SqlParity.Providers;
        var html = DispatchProxy.Create<IHtmlHelper, FakeHtml>();
        Expression<Func<TheirModel, Olive.Mvc.BlobViewModel>> theirProperty = v => v.Photo;
        Expression<Func<OurModel, Platform.Mvc.BlobViewModel>> ourProperty = v => v.Photo;
        foreach (var attributes in new object[] { null, new { @class = "x", disabled = "disabled" } })
        {
            var theirs = Text(new Olive.Mvc.DefaultFileUploadMarkupGenerator().Generate(html, new TheirModel { Photo = new Olive.Mvc.BlobViewModel { Filename = file, Url = url } }, theirProperty, attributes));
            var ours = Text(new Platform.Mvc.DefaultFileUploadMarkupGenerator().Generate(html, new OurModel { Photo = new Platform.Mvc.BlobViewModel { Filename = file, Url = url } }, ourProperty, attributes));
            var at = ours.Zip(theirs).TakeWhile(p => p.First == p.Second).Count();
            if (ours != theirs) Assert.Fail("ours:  " + ours[Math.Max(0, at - 40)..] + "\nOlive: " + theirs[Math.Max(0, at - 40)..]);
            await Task.CompletedTask;
        }
    }
}
