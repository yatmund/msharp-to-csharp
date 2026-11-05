using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace CompatTests;

public class MvcValueParity
{
    static string Run(Func<object> f)
    {
        try { return f() switch { null => "null", IEnumerable<string> list => string.Join("|", list), var v => v.ToString() }; }
        catch (Exception e) { return "throws " + e.GetType().Name; }
    }

    // Olive throwing isn't behaviour to copy, so only its values are compared.
    static async Task Same(Func<object> ours, Func<object> theirs)
    {
        var expected = Run(theirs);
        if (!expected.StartsWith("throws ")) await Assert.That(Run(ours)).IsEqualTo(expected);
    }

    public static IEnumerable<string> Texts() => ["true", "false", "True", "FALSE", " true ", "", " ", "null", "Null", "NULL", "yes", "Yes", "no", "No", "on", "off", "y", "n", "1", "0", "x"];

    [Test]
    [MethodDataSource(nameof(Texts))]
    public async Task OptionalBooleanFilter_matches(string text)
    {
        await Same(() => Platform.Mvc.OptionalBooleanFilter.Parse(text), () => Olive.Mvc.OptionalBooleanFilter.Parse(text));
        await Same(() => Platform.Mvc.OptionalBooleanFilter.Parse(text)?.Value, () => Olive.Mvc.OptionalBooleanFilter.Parse(text)?.Value);
        await Same(() => TypeDescriptor.GetConverter(typeof(Platform.Mvc.OptionalBooleanFilter)).ConvertFrom(text), () => TypeDescriptor.GetConverter(typeof(Olive.Mvc.OptionalBooleanFilter)).ConvertFrom(text));
    }

    [Test]
    public async Task OptionalBooleanFilter_constants_match()
    {
        await Same(() => Platform.Mvc.OptionalBooleanFilter.Null, () => Olive.Mvc.OptionalBooleanFilter.Null);
        await Same(() => Platform.Mvc.OptionalBooleanFilter.True, () => Olive.Mvc.OptionalBooleanFilter.True);
        await Same(() => Platform.Mvc.OptionalBooleanFilter.False, () => Olive.Mvc.OptionalBooleanFilter.False);
        await Assert.That(TypeDescriptor.GetConverter(typeof(Platform.Mvc.OptionalBooleanFilter)).CanConvertFrom(typeof(string)))
            .IsEqualTo(TypeDescriptor.GetConverter(typeof(Olive.Mvc.OptionalBooleanFilter)).CanConvertFrom(typeof(string)));
    }

    public static IEnumerable<(string Query, string Form, string Key)> Requests() =>
    [
        ("q=query", "q=form", "q"), ("q=query", "Q=form", "q"), ("", "q=form", "q"), ("q=query", "", "q"), ("q=a&q=b", "", "q"), ("Q=query", "", "q"),
    ];

    static HttpRequest Request(string query, string form)
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString("?" + query);
        if (form.Length > 0)
        {
            http.Request.ContentType = "application/x-www-form-urlencoded";
            http.Request.Form = new FormCollection(form.Split('&').Select(p => p.Split('=')).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => new StringValues(g.Select(p => p[1]).ToArray())));
        }
        return http.Request;
    }

    [Test]
    [MethodDataSource(nameof(Requests))]
    public async Task Request_param_matches(string query, string form, string key) =>
        await Same(() => Platform.WebExtensions.Param(Request(query, form), key), () => Olive.OliveWebExtensions.Param(Request(query, form), key));

    [Test]
    public async Task ColumnSelection_matches()
    {
        var theirs = new Olive.Mvc.ColumnSelection("p") { Options = ["A", "B"], Default = ["A"] };
        var ours = new Platform.Mvc.ColumnSelection("p") { Options = ["A", "B"], Default = ["A"] };
        await Same(() => ours.Current, () => theirs.Current);
        await Same(() => ours.CookieKey, () => theirs.CookieKey);
        await Same(() => string.Join(",", ours.GetListOptions().Select(o => o.Text + "=" + o.Value + ":" + o.Selected)), () => string.Join(",", theirs.GetListOptions().Select(o => o.Text + "=" + o.Value + ":" + o.Selected)));
    }
}
