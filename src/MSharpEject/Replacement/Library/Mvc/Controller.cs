using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Platform.Entities;

namespace Platform.Mvc
{
    public enum PageLifecycleStage { PreInit = 0, Init = 1 }

    public enum WindowAction { CloseModal = 0, CloseModalRebindParent = 1, CloseModalRefreshParent = 2, Refresh = 3, Close = 4, Back = 5, Print = 6, ShowPleaseWait = 7 }

    public class JavascriptDependency
    {
        public List<JavascriptDependency> Dependencies = new List<JavascriptDependency>();

        public JavascriptDependency(string url) => Url = url;

        public string Url { get; }

        public static JavascriptDependency Alias(string alias) => new JavascriptDependency(alias);

        public static JavascriptDependency Relative(string relativePath) => new JavascriptDependency(relativePath);

        public static JavascriptDependency Absolute(string relativeOrAbsolutePath) => new JavascriptDependency(relativeOrAbsolutePath);

        public JavascriptDependency Add(JavascriptDependency dependency)
        {
            Dependencies.Add(dependency);
            return this;
        }
    }

    public class JavascriptModule : JavascriptDependency
    {
        JavascriptModule(string url) : base(url) { }

        public static new JavascriptModule Alias(string alias) => new JavascriptModule(alias);

        public static new JavascriptModule Relative(string relativePath) => new JavascriptModule(relativePath);

        public static new JavascriptModule Absolute(string relativeOrAbsolutePath) => new JavascriptModule(relativeOrAbsolutePath);

        public new JavascriptModule Add(JavascriptDependency dependency)
        {
            Dependencies.Add(dependency);
            return this;
        }

        public string GenerateLoad(string staticFunctionInvokation = "run()") =>
            Dependencies.Any()
                ? $"requirejs([{string.Join(", ", Dependencies.Select(d => "'" + d.Url + "'"))}], () => requirejs(['{Url}'], m => m.default.{staticFunctionInvokation}));"
                : $"requirejs(['{Url}'], m => m.default.{staticFunctionInvokation});";
    }

    public class JavascriptService
    {
        public JavascriptService(string key, string function, params object[] arguments)
        {
            ServiceKey = key;
            Function = function;
            Arguments = arguments;
        }

        public string ServiceKey { get; }
        public string Function { get; }
        public object[] Arguments { get; }

        public static bool operator ==(JavascriptService me, JavascriptService other) =>
            ReferenceEquals(me, other) || (!ReferenceEquals(me, null) && !ReferenceEquals(other, null) && me.ServiceKey == other.ServiceKey && me.Function == other.Function);

        public static bool operator !=(JavascriptService me, JavascriptService other) => !(me == other);

        public override bool Equals(object obj) => obj is JavascriptService other && this == other;

        public override int GetHashCode() => (ServiceKey + "|" + Function).GetHashCode();
    }

    /// <summary>Writes the value with Newtonsoft and Pascal case names, whatever the app's JSON settings; the front end expects that shape.</summary>
    public class JsonNetResult : JsonResult
    {
        public JsonNetResult(object value) : this(value, new JsonSerializerSettings()) { }

        public JsonNetResult(object value, object serializerSettings) : base(value) => Settings = serializerSettings as JsonSerializerSettings ?? new JsonSerializerSettings();

        public JsonSerializerSettings Settings { get; }

        public override async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.ContentType = ContentType ?? "application/json; charset=utf-8";
            if (StatusCode.HasValue) response.StatusCode = StatusCode.Value;
            await response.WriteAsync(JsonConvert.SerializeObject(Value, Settings));
        }
    }

    public class TextActionResult : IActionResult
    {
        public TextActionResult(string message, HttpStatusCode statusCode)
        {
            Message = message;
            StatusCode = statusCode;
        }

        public string Message { get; }

        public HttpStatusCode StatusCode { get; }

        public virtual async Task ExecuteResultAsync(ActionContext context)
        {
            context.HttpContext.Response.StatusCode = (int)StatusCode;
            context.HttpContext.Response.ContentType = "text/plain";
            await context.HttpContext.Response.WriteAsync(Message ?? string.Empty);
        }
    }

    public class NotFoundTextActionResult : TextActionResult
    {
        public NotFoundTextActionResult(string message) : base(message, HttpStatusCode.NotFound) { }
    }

    public class UnauthorizedTextActionResult : TextActionResult
    {
        public UnauthorizedTextActionResult(string message) : base(message, HttpStatusCode.Unauthorized) { }
    }

    static class ActionQueue
    {
        const string Key = "Platform.Mvc.JavascriptActions";

        internal static List<object> Of(HttpContext http)
        {
            if (!(http.Items[Key] is List<object> actions)) http.Items[Key] = actions = new List<object>();
            return actions;
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public abstract class Controller : Microsoft.AspNetCore.Mvc.Controller
    {
        protected Controller() { }

        public static IDatabase Database => Context.Current.Database();

        public new HttpContext HttpContext => base.HttpContext ?? Context.Current.Http();

        public new HttpRequest Request => HttpContext?.Request;

        public HtmlHelper Html => HttpContext.RequestServices.GetService<IHtmlHelper>() as HtmlHelper;

        protected virtual bool IsMicrofrontEnd => false;

        public ILogger Log => Platform.Log.For(GetType());

        public virtual string BrowserTitle
        {
            get => ViewData["Title"] as string;
            set => ViewData["Title"] = value;
        }

        protected JsonResult AddAction(object action)
        {
            ActionQueue.Of(HttpContext).Add(action);
            return null;
        }

        protected JsonResult JsonActions()
        {
            var actions = ActionQueue.Of(HttpContext);
            var result = new JsonNetResult(actions.ToArray());
            actions.Clear();
            return result;
        }

        protected ActionResult JsonActions(IViewModel info) => JsonActions();

        protected internal new ViewResult View() => base.View();

        protected internal async Task<TViewModel> Bind<TViewModel>(string prefix = "") where TViewModel : class, new()
        {
            var result = new TViewModel();
            await TryUpdateModelAsync(result, prefix);
            return result;
        }

        public async Task<JsonResult> Json<TResult>(Task<TResult> data) => new JsonNetResult(await data);

        public JsonResult JavaScript(string script, PageLifecycleStage stage = PageLifecycleStage.Init)
        {
            AddAction(new { Script = script, Stage = stage.ToString() });
            return JsonActions();
        }

        public JsonResult JavaScript(JavascriptModule module, string staticFunctionInvokation = "run()") => JavaScript(module.GenerateLoad(staticFunctionInvokation));

        public virtual JsonResult JavaScript(JavascriptService service)
        {
            AddAction(new { service.ServiceKey, service.Function, service.Arguments });
            return JsonActions();
        }

        public JsonResult JavaScript(JavascriptDependency[] dependencies, string javascriptCode) =>
            JavaScript($"requirejs([{string.Join(", ", dependencies.Select(d => "'" + d.Url + "'"))}], () => {{ {javascriptCode} }});");

        public virtual void LoadJavascriptModule(string relativePath, string staticFunctionInvokation = "run()") =>
            AddAction(new { Script = JavascriptModule.Relative(relativePath).GenerateLoad(staticFunctionInvokation), Stage = PageLifecycleStage.Init.ToString() });

        public override ViewResult View(string viewName) => base.View(viewName);

        public override ViewResult View(string viewName, object model) => base.View(viewName, model);

        public override Task<bool> TryUpdateModelAsync<TModel>(TModel model) => base.TryUpdateModelAsync(model);

        protected JsonResult Notify(object message, bool obstruct = true) => Notify(message, null, obstruct);

        protected JsonResult Notify(object message, string style, bool obstruct = true)
        {
            AddAction(new { Notify = message?.ToString(), Obstruct = obstruct, Style = style });
            return JsonActions();
        }

        public ActionResult AjaxRedirect(string url, string target = null)
        {
            if (!Request.IsAjaxRequest()) return base.Redirect(url);
            AddAction(new { Redirect = url, WithAjax = true, Target = target });
            return JsonActions();
        }

        public ActionResult Redirect(string url, string target = null)
        {
            if (!Request.IsAjaxRequest()) return base.Redirect(url);
            AddAction(new { Redirect = url, WithAjax = false, Target = target });
            return JsonActions();
        }

        protected JsonResult Do(WindowAction action)
        {
            AddAction(new { BrowserAction = action.ToString() });
            return JsonActions();
        }

        protected JsonResult ReplaceView(string text, bool htmlEncode = true)
        {
            AddAction(new { ReplaceView = htmlEncode ? WebUtility.HtmlEncode(text) : text });
            return JsonActions();
        }

        protected JsonResult ReplaceSource<T>(string controlId, IEnumerable<SelectListItem> newItems)
        {
            AddAction(new { ReplaceSource = controlId, Items = newItems.ToArray() });
            return JsonActions();
        }

        protected ActionResult SubFormView<TViewModel>(string subFormName, string subFormView) where TViewModel : new() =>
            SubFormView(subFormName, subFormView, new TViewModel());

        protected JsonResult SubFormView(string subFormName, string subFormView, object viewModel)
        {
            AddAction(new { SubForm = subFormName, Html = RenderPartialView(subFormView, viewModel).GetAwaiter().GetResult() });
            return JsonActions();
        }

        protected Task<string> RenderPartialView(string viewName, object model) =>
            HttpContext.RequestServices.GetRequiredService<IViewRenderService>().RenderToStringAsync(viewName, model);

        protected virtual ActionResult RedirectToLogin() => Redirect("/login?ReturnUrl=" + WebUtility.UrlEncode(Request.ToPathAndQuery()));

        public NotFoundTextActionResult NotFound(string message) => new NotFoundTextActionResult(message);

        public UnauthorizedTextActionResult Unauthorized(string message) => new UnauthorizedTextActionResult(message);

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (!await AuthorizeRequestParams(context))
            {
                context.Result = Unauthorized("Unauthorized request parameters.");
                return;
            }
            if (BrowserTitle == null) BrowserTitle = GetDefaultBrowserTitle(context);
            await BindAttributeRunner.Run(context);
            await base.OnActionExecutionAsync(context, next);
        }

        protected virtual string GetDefaultBrowserTitle(ActionExecutingContext context) => Config.Get("Mvc:DefaultBrowserTitle") ?? Config.Get("Mvc:SiteName");

        protected virtual Task<bool> AuthorizeRequestParams(ActionExecutingContext context) => Task.FromResult(true);

        protected async Task<ActionResult> File(Blob file, string downloadFileName = null, System.Net.Http.Headers.CacheControlHeaderValue cacheControl = null, bool showInline = false)
        {
            var data = await file.GetFileDataAsync();
            if (cacheControl != null) Response.Headers[HeaderNames.CacheControl] = cacheControl.ToString();
            var name = downloadFileName ?? file.FileName;
            Response.Headers[HeaderNames.ContentDisposition] = new ContentDispositionHeaderValue(showInline ? "inline" : "attachment") { FileName = name }.ToString();
            return base.File(data, file.GetMimeType());
        }

        protected async Task<JsonResult> NonobstructiveFile(byte[] data, string filename)
        {
            var action = await HttpContext.RequestServices.GetRequiredService<IFileRequestService>().CreateDownloadAction(data, filename);
            AddAction(action);
            return JsonActions();
        }

        protected async Task<JsonResult> NonobstructiveFile(Blob file, string downloadFileName = null) =>
            await NonobstructiveFile(await file.GetFileDataAsync(), downloadFileName ?? file.FileName);

        protected Task<JsonResult> NonobstructiveFile(FileInfo file) => NonobstructiveFile(System.IO.File.ReadAllBytes(file.FullName), file.Name);
    }

    public abstract class ViewComponent : Microsoft.AspNetCore.Mvc.ViewComponent
    {
        protected ViewComponent() { }

        protected IDatabase Database => Context.Current.Database();

        public virtual void LoadJavascriptModule(string relativePath, string staticFunctionInvokation = "run()") =>
            JavaScript(JavascriptModule.Relative(relativePath), staticFunctionInvokation);

        public void JavaScript(JavascriptModule module, string staticFunctionInvokation = "run()") => JavaScript(module.GenerateLoad(staticFunctionInvokation));

        public void JavaScript(string script, PageLifecycleStage stage = PageLifecycleStage.Init) =>
            ActionQueue.Of(HttpContext).Add(new { Script = script, Stage = stage.ToString() });

        public void JavaScript(JavascriptDependency[] dependencies, string javascriptCode) =>
            JavaScript($"requirejs([{string.Join(", ", dependencies.Select(d => "'" + d.Url + "'"))}], () => {{ {javascriptCode} }});");

        protected async Task<TViewModel> Bind<TViewModel>(TViewModel instance = null) where TViewModel : class, IViewModel, new()
        {
            instance = instance ?? new TViewModel();
            await new DefaultModelBinding(ViewContext.HttpContext).Bind(instance);
            return instance;
        }
    }

    sealed class DefaultModelBinding
    {
        readonly HttpContext http;

        public DefaultModelBinding(HttpContext http) => this.http = http;

        public async Task Bind(object model)
        {
            foreach (var property in model.GetType().GetProperties().Where(p => p.CanWrite))
            {
                var value = http.Request.Param(property.Name);
                if (value == null) continue;
                property.SetValue(model, await ViewModelServices.Convert(value, property.PropertyType));
            }
        }
    }
}
