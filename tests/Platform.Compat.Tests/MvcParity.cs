using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace CompatTests;

/// <summary>Controller actions must produce the JSON the Olive.MvcJs front end reads.</summary>
public class MvcParity
{
    public class OliveProbe : Olive.Mvc.Controller
    {
        public object CallNotify(object m, bool o = true) => Notify(m, o);
        public object CallNotify(object m, string s, bool o = true) => Notify(m, s, o);
        public object CallDo(Olive.Mvc.WindowAction a) => Do(a);
        public object CallReplaceView(string t, bool e = true) => ReplaceView(t, e);
        public object CallReplaceSource(string id, IEnumerable<SelectListItem> items) => ReplaceSource<object>(id, items);
        public object CallAddAction(object a) => AddAction(a);
        public object CallJsonActions() => JsonActions();
    }

    public class OurProbe : Platform.Mvc.Controller
    {
        public object CallNotify(object m, bool o = true) => Notify(m, o);
        public object CallNotify(object m, string s, bool o = true) => Notify(m, s, o);
        public object CallDo(Platform.Mvc.WindowAction a) => Do(a);
        public object CallReplaceView(string t, bool e = true) => ReplaceView(t, e);
        public object CallReplaceSource(string id, IEnumerable<SelectListItem> items) => ReplaceSource<object>(id, items);
        public object CallAddAction(object a) => AddAction(a);
        public object CallJsonActions() => JsonActions();
    }

    static string Run<TController>(Func<TController, object> act, IServiceProvider provider) where TController : Controller, new()
    {
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        var controller = new TController { ControllerContext = new ControllerContext { HttpContext = http } };
        try
        {
            return act(controller) switch
            {
                JsonResult json => JsonConvert.SerializeObject(json.Value),
                null => "null",
                var other => other.GetType().Name + " " + JsonConvert.SerializeObject(other),
            };
        }
        catch (Exception e)
        {
            return "throws " + e.GetType().Name;
        }
    }

    static readonly SelectListItem[] Items = [new("A", "1"), new("B", "2", true)];

    public static IEnumerable<string> Cases() => Pairs.Keys;

    static readonly Dictionary<string, (Func<OliveProbe, object> Olive, Func<OurProbe, object> Ours)> Pairs = new()
    {
        ["notify"] = (c => c.CallNotify("Saved!"), c => c.CallNotify("Saved!")),
        ["notify-style"] = (c => c.CallNotify("Oops", "error", false), c => c.CallNotify("Oops", "error", false)),
        ["redirect"] = (c => c.Redirect("/home", null), c => c.Redirect("/home", null)),
        ["redirect-target"] = (c => c.Redirect("/home", "_blank"), c => c.Redirect("/home", "_blank")),
        ["ajax-redirect"] = (c => c.AjaxRedirect("/x", "modal"), c => c.AjaxRedirect("/x", "modal")),
        ["close-modal"] = (c => c.CallDo(Olive.Mvc.WindowAction.CloseModal), c => c.CallDo(Platform.Mvc.WindowAction.CloseModal)),
        ["refresh-parent"] = (c => c.CallDo(Olive.Mvc.WindowAction.CloseModalRefreshParent), c => c.CallDo(Platform.Mvc.WindowAction.CloseModalRefreshParent)),
        ["please-wait"] = (c => c.CallDo(Olive.Mvc.WindowAction.ShowPleaseWait), c => c.CallDo(Platform.Mvc.WindowAction.ShowPleaseWait)),
        ["replace-view"] = (c => c.CallReplaceView("<b>x</b>"), c => c.CallReplaceView("<b>x</b>")),
        ["replace-view-raw"] = (c => c.CallReplaceView("<b>x</b>", false), c => c.CallReplaceView("<b>x</b>", false)),
        ["script"] = (c => c.JavaScript("alert(1)", Olive.Mvc.PageLifecycleStage.Init), c => c.JavaScript("alert(1)", Platform.Mvc.PageLifecycleStage.Init)),
        ["script-preinit"] = (c => c.JavaScript("alert(2)", Olive.Mvc.PageLifecycleStage.PreInit), c => c.JavaScript("alert(2)", Platform.Mvc.PageLifecycleStage.PreInit)),
        ["service"] = (c => c.JavaScript(new Olive.Mvc.JavascriptService("svc", "fn", 1, "a")), c => c.JavaScript(new Platform.Mvc.JavascriptService("svc", "fn", 1, "a"))),
        ["module"] = (c => c.JavaScript(Olive.Mvc.JavascriptModule.Relative("/scripts/m.js"), "go()"), c => c.JavaScript(Platform.Mvc.JavascriptModule.Relative("/scripts/m.js"), "go()")),
        ["replace-source"] = (c => c.CallReplaceSource("Ctl", Items), c => c.CallReplaceSource("Ctl", Items)),
        ["queued"] = (c => { c.CallNotify("one"); c.CallAddAction(new { Custom = 1 }); return c.Redirect("/z", null); },
                      c => { c.CallNotify("one"); c.CallAddAction(new { Custom = 1 }); return c.Redirect("/z", null); }),
        ["empty"] = (c => c.CallJsonActions(), c => c.CallJsonActions()),
        ["not-found"] = (c => c.NotFound("nope"), c => c.NotFound("nope")),
        ["unauthorized"] = (c => c.Unauthorized("no"), c => c.Unauthorized("no")),
    };

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task Action_json_matches(string name)
    {
        var (olive, ours) = Pairs[name];
        var theirs = Run(olive, SqlParity.Providers.Olive).Replace("NotFoundTextActionResult", "X").Replace("UnauthorizedTextActionResult", "X");
        var mine = Run(ours, SqlParity.Providers.Ours).Replace("NotFoundTextActionResult", "X").Replace("UnauthorizedTextActionResult", "X");
        await Assert.That(mine).IsEqualTo(theirs);
    }
}
