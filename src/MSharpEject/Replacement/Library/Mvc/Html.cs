using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Platform.Entities;

namespace Platform.Mvc
{
    public static class MvcExtensions
    {
        public static HtmlString Replace(this IHtmlContent @this, string oldText, string newText) => new HtmlString(Render(@this).Replace(oldText, newText));

        public static HtmlString PrefixName(this IHtmlContent @this, string prefix) =>
            new HtmlString(Render(@this).Replace("name=\"", "name=\"" + prefix + ".").Replace("id=\"", "id=\"" + prefix + "_"));

        static string Render(IHtmlContent content)
        {
            using (var writer = new StringWriter())
            {
                content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
                return writer.ToString();
            }
        }

        public static bool IsInvisible(this IViewModel @this, string propertyName) =>
            @this.GetType().GetProperty(propertyName + "_Visible")?.GetValue(@this) is false;

        public static bool IsValid(this ModelStateDictionary @this, IViewModel viewModel) => @this.IsValid;

        public static IEnumerable<string> GetErrors(this ModelStateDictionary @this, bool errorStack = false) =>
            @this.Values.SelectMany(v => v.Errors).Select(e => errorStack && e.Exception != null ? e.Exception.ToString() : e.ErrorMessage.Or(e.Exception?.Message));

        public static HtmlString Raw(this string @this) => new HtmlString(@this);

        public static dynamic ViewBag(this HttpContext @this)
        {
            if (!(@this.Items["ViewBag"] is ExpandoObject bag)) @this.Items["ViewBag"] = bag = new ExpandoObject();
            return bag;
        }

        public static string GetCurrentControllerName(this ActionContext @this) => @this.RouteData.Values["controller"]?.ToString();

        public static T OrderBy<T>(this T query, ListSortExpression sort) where T : IDatabaseQuery
        {
            if (!string.IsNullOrEmpty(sort?.Expression)) query.OrderBy(sort.Expression, sort.Descending);
            return query;
        }

        public static T Sort<T>(this T @this, ListSortExpression sort) where T : IDatabaseQuery => @this.OrderBy(sort);

        public static IMvcBuilder Mvc(this Context @this) => Startup.MvcBuilder;

        public static TAttribute GetAttribute<TAttribute>(this ModelMetadata @this) where TAttribute : Attribute =>
            @this.ContainerType?.GetProperty(@this.PropertyName ?? "")?.GetCustomAttribute<TAttribute>() ?? @this.ModelType.GetCustomAttribute<TAttribute>();

        public static TUser Extract<TUser>(this ClaimsPrincipal @this) where TUser : IEntity
        {
            var id = @this?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return string.IsNullOrEmpty(id) ? default(TUser) : (TUser)Context.Current.Database().GetOrDefault(id, typeof(TUser)).GetAwaiter().GetResult();
        }

        public static HtmlString ToIcon(this Exception @this, string errorMessage = null) =>
            new HtmlString($"<span class=\"error-icon\" title=\"{WebUtility.HtmlEncode(errorMessage ?? @this?.Message)}\">&#9888;</span>");

        public static T GetViewModel<T>(this ActionExecutingContext @this, string key = "info") where T : class, IViewModel =>
            @this.ActionArguments.TryGetValue(key, out var value) ? value as T : null;

        public static HtmlString ToJson(this IHtmlHelper @this, object obj) => new HtmlString(JsonConvert.SerializeObject(obj));

        public static HtmlString GetActionsJson(this IHtmlHelper @this) => new HtmlString(JsonConvert.SerializeObject(ActionQueue.Of(@this.ViewContext.HttpContext)));

        public static IHtmlContent RadioButtonsFor<TModel, TProperty>(this IHtmlHelper<TModel> @this, Expression<Func<TModel, TProperty>> property, IEnumerable<SelectListItem> selectList, object htmlAttributes)
        {
            var name = @this.NameFor(property);
            var current = @this.ValueFor(property);
            var html = new StringBuilder("<div class=\"radio-list\"" + ToHtmlAttributes(htmlAttributes) + ">");
            var index = 0;
            foreach (var item in selectList)
            {
                var id = name.Replace(".", "_") + "_" + index++;
                var selected = item.Selected || item.Value == current;
                html.Append($"<div class=\"radio-item\"><input type=\"radio\" id=\"{id}\" name=\"{name}\" value=\"{WebUtility.HtmlEncode(item.Value)}\"{(selected ? " checked=\"checked\"" : "")} /><label for=\"{id}\">{WebUtility.HtmlEncode(item.Text)}</label></div>");
            }
            return new HtmlString(html.Append("</div>").ToString());
        }

        public static IHtmlContent FileUploadFor<TModel>(this IHtmlHelper<TModel> @this, Expression<Func<TModel, IEnumerable<BlobViewModel>>> property, object htmlAttributes = null) =>
            @this.ViewContext.HttpContext.RequestServices.GetRequiredService<IFileUploadMarkupGenerator>().Generate(@this, @this.ViewData.Model, property, htmlAttributes);

        public static IHtmlContent FileUploadFor<TModel>(this IHtmlHelper<TModel> @this, Expression<Func<TModel, BlobViewModel>> property, object htmlAttributes = null) =>
            @this.ViewContext.HttpContext.RequestServices.GetRequiredService<IFileUploadMarkupGenerator>().Generate(@this, @this.ViewData.Model, property, htmlAttributes);

        public static HtmlString CheckBoxesFor<TModel, TProperty>(this IHtmlHelper<TModel> @this, Expression<Func<TModel, TProperty>> property, IEnumerable<SelectListItem> selectList, object htmlAttributes = null)
        {
            var name = @this.NameFor(property);
            var member = (property.Body as MemberExpression)?.Member as PropertyInfo;
            var value = member != null && @this.ViewData.Model != null ? member.GetValue(@this.ViewData.Model) as IEnumerable : null;
            return @this.CheckBoxes(name, value, selectList, htmlAttributes, member);
        }

        public static HtmlString CheckBoxes(this IHtmlHelper @this, string name, IEnumerable selectedItems, IEnumerable<SelectListItem> selectList, object htmlAttributes = null, PropertyInfo property = null)
        {
            var selected = new HashSet<string>((selectedItems ?? new object[0]).Cast<object>().Select(i => i is IEntity e ? e.GetId()?.ToString() : i?.ToString()));
            var html = new StringBuilder("<div class=\"checkbox-list\"" + ToHtmlAttributes(htmlAttributes) + ">");
            var index = 0;
            foreach (var item in selectList)
            {
                var id = name.Replace(".", "_") + "_" + index++;
                var isChecked = item.Selected || selected.Contains(item.Value);
                html.Append($"<div class=\"checkbox-item\"><input type=\"checkbox\" id=\"{id}\" name=\"{name}\" value=\"{WebUtility.HtmlEncode(item.Value)}\"{(isChecked ? " checked=\"checked\"" : "")} /><label for=\"{id}\">{WebUtility.HtmlEncode(item.Text)}</label></div>");
            }
            return new HtmlString(html.Append("</div>").ToString());
        }

        public static string ToHtmlAttributes(object htmlAttributes)
        {
            if (htmlAttributes == null) return string.Empty;
            var pairs = htmlAttributes is IDictionary<string, object> dict ? dict.Select(p => (p.Key, p.Value))
                : htmlAttributes.GetType().GetProperties().Select(p => (Key: p.Name.Replace("_", "-"), Value: p.GetValue(htmlAttributes)));
            return string.Concat(pairs.Where(p => p.Value != null).Select(p => $" {p.Key}=\"{WebUtility.HtmlEncode(p.Value.ToString())}\""));
        }

        public static IHtmlContent Concat(this IHtmlContent @this, IHtmlContent first, params IHtmlContent[] others)
        {
            var builder = new HtmlContentBuilder();
            builder.AppendHtml(@this);
            builder.AppendHtml(first);
            foreach (var other in others) builder.AppendHtml(other);
            return builder;
        }

        public static IHtmlContent RegisterStartupActions(this IHtmlHelper @this) =>
            new HtmlString($"<input type=\"hidden\" class=\"startup-actions\" value=\"{WebUtility.HtmlEncode(JsonConvert.SerializeObject(ActionQueue.Of(@this.ViewContext.HttpContext)))}\" />");

        public static HttpRequest Request(this IHtmlHelper @this) => @this.ViewContext.HttpContext.Request;

        public static IHtmlContent StartupActionsJson(this IHtmlHelper @this) => new HtmlString(JsonConvert.SerializeObject(ActionQueue.Of(@this.ViewContext.HttpContext)));

        public static HtmlString RunJavascript(this IHtmlHelper @this, string script, PageLifecycleStage stage = PageLifecycleStage.Init) => @this.RunJavascript(script, script, stage);

        public static HtmlString RunJavascript(this IHtmlHelper @this, string key, string script, PageLifecycleStage stage = PageLifecycleStage.Init)
        {
            var http = @this.ViewContext.HttpContext;
            var keys = http.Items["Platform.Mvc.ScriptKeys"] as HashSet<string> ?? new HashSet<string>();
            http.Items["Platform.Mvc.ScriptKeys"] = keys;
            if (keys.Add(key)) ActionQueue.Of(http).Add(new { Script = script, Stage = stage.ToString() });
            return HtmlString.Empty;
        }

        public static HtmlString RunJavascript(this IHtmlHelper @this, JavascriptService service, PageLifecycleStage stage = PageLifecycleStage.Init)
        {
            ActionQueue.Of(@this.ViewContext.HttpContext).Add(new { service.ServiceKey, service.Function, service.Arguments });
            return HtmlString.Empty;
        }

        public static HtmlString ReferenceScriptFile(this IHtmlHelper @this, string scriptUrl)
        {
            var http = @this.ViewContext.HttpContext;
            var files = http.Items["Platform.Mvc.ScriptFiles"] as List<string> ?? new List<string>();
            http.Items["Platform.Mvc.ScriptFiles"] = files;
            if (!files.Contains(scriptUrl)) files.Add(scriptUrl);
            return HtmlString.Empty;
        }

        public static HtmlString RegisterDynamicScriptFiles(this IHtmlHelper @this) =>
            new HtmlString(string.Concat((@this.ViewContext.HttpContext.Items["Platform.Mvc.ScriptFiles"] as List<string> ?? new List<string>())
                .Select(f => $"<script type=\"text/javascript\" src=\"{f}\"></script>")));

        public static void AddRange<T>(this IList<SelectListItem> listItems, IEnumerable<T> items, Func<T, object> displayExpression) where T : IEntity =>
            listItems.AddRange(items, default(T), displayExpression);

        public static void AddRange<T>(this IList<SelectListItem> listItems, IEnumerable<T> items, T selectedItem, Func<T, object> displayExpression) where T : IEntity
        {
            foreach (var item in items)
                listItems.Add(new SelectListItem(displayExpression(item)?.ToString(), item.GetId()?.ToString(), item.Equals(selectedItem)));
        }

        public static void AddRange<T>(this IList<SelectListItem> listItems, IEnumerable<T> items, Func<T, object> displayExpression, Func<T, object> valueExpression)
        {
            foreach (var item in items) listItems.Add(new SelectListItem(displayExpression(item)?.ToString(), valueExpression(item)?.ToString()));
        }

        public static void AddRange(this IList<SelectListItem> listItems, IEnumerable items) => listItems.AddRange(items, (IEntity)null);

        public static void AddRange(this IList<SelectListItem> listItems, IEnumerable items, IEntity selectedItem)
        {
            foreach (var item in items) listItems.Add(ToItem(item, Equals(item, selectedItem)));
        }

        public static void AddRange<T>(this IList<SelectListItem> listItems, IEnumerable<T> items, IEnumerable<T> selectedItems, Func<T, object> displayExpression) where T : IEntity
        {
            var selected = selectedItems?.ToList() ?? new List<T>();
            foreach (var item in items) listItems.Add(new SelectListItem(displayExpression(item)?.ToString(), item.GetId()?.ToString(), selected.Contains(item)));
        }

        public static void Add(this IList<SelectListItem> items, IEntity entity) => items.Add(ToItem(entity, false));

        public static void Add(this IList<SelectListItem> items, object text, object value) => items.Add(new SelectListItem(text?.ToString(), value?.ToString()));

        public static void AddRange(this IList<SelectListItem> listItems, IEnumerable items, IEnumerable selectedItems)
        {
            var selected = (selectedItems ?? new object[0]).Cast<object>().ToList();
            foreach (var item in items) listItems.Add(ToItem(item, selected.Contains(item)));
        }

        static SelectListItem ToItem(object item, bool selected) =>
            new SelectListItem(item?.ToString(), item is IEntity e ? e.GetId()?.ToString() : item?.ToString(), selected);

        public static string GetSelected(this SelectListItem item) => item.Selected ? "selected" : null;

        public static List<SelectListItem> Clone(this IEnumerable<SelectListItem> items) =>
            items.Select(i => new SelectListItem(i.Text, i.Value, i.Selected, i.Disabled) { Group = i.Group }).ToList();

        public static void SetSelected(this IEnumerable<SelectListItem> items, params string[] selectedValues)
        {
            foreach (var item in items) item.Selected = selectedValues.Contains(item.Value);
        }

        public static List<SelectListItem> ToSelectListItems(this IEnumerable<IEntity> items) => items.Select(i => ToItem(i, false)).ToList();

        public static void AddRange<TEnum>(this List<SelectListItem> items) where TEnum : struct, IConvertible
        {
            foreach (var value in Enum.GetValues(typeof(TEnum))) items.Add(new SelectListItem(value.ToString().ToLiteralFromPascalCase(), value.ToString()));
        }

        public static string Current(this IUrlHelper @this) => @this.ActionContext.HttpContext.Request.ToPathAndQuery();

        public static string AsSafeReturnUrl(this IUrlHelper @this) => WebUtility.UrlEncode(@this.Current());

        public static string Current(this IUrlHelper @this, object queryParameters) =>
            @this.Current(queryParameters.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(queryParameters)?.ToString()));

        public static string Current(this IUrlHelper @this, IDictionary<string, string> queryParameters)
        {
            var request = @this.ActionContext.HttpContext.Request;
            var query = request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            foreach (var p in queryParameters)
                if (p.Value == null) query.Remove(p.Key);
                else query[p.Key] = p.Value;
            return request.Path + (query.Count == 0 ? "" : "?" + string.Join("&", query.Select(q => q.Key + "=" + Uri.EscapeDataString(q.Value))));
        }

        public static Uri CurrentUri(this IUrlHelper @this) => new Uri(@this.ActionContext.HttpContext.Request.ToAbsoluteUri());

        public static string QueryString(this IUrlHelper @this) => @this.ActionContext.HttpContext.Request.QueryString.Value;

        public static string ReturnUrl(this IUrlHelper @this) => @this.ActionContext.HttpContext.Request.GetReturnUrl();

        public static string ActionWithQuery(this IUrlHelper @this, string actionUrl, IEntity listItem = null)
        {
            var query = @this.ActionContext.HttpContext.Request.QueryString.Value ?? "";
            var url = actionUrl + (query.Length > 0 ? (actionUrl.Contains("?") ? "&" + query.TrimStart('?') : query) : "");
            return listItem == null ? url : url + (url.Contains("?") ? "&" : "?") + "list.item=" + listItem.GetId();
        }

        public static string ActionWithQuery(this IUrlHelper @this, string actionUrl, object query)
        {
            var extra = string.Join("&", query.GetType().GetProperties().Select(p => p.Name + "=" + Uri.EscapeDataString(p.GetValue(query)?.ToString() ?? "")));
            return actionUrl + (actionUrl.Contains("?") ? "&" : "?") + extra;
        }

        public static bool Has(this ActionContext @this, string key) => @this.HttpContext.Request.Has(key);

        public static bool Lacks(this ActionContext @this, string key) => !@this.Has(key);

        public static string Param(this ActionContext @this, string key) => @this.HttpContext.Request.Param(key);

        public static async Task<IEnumerable<T>> GetList<T>(this HttpRequest @this, string key, char separator = ',') where T : IEntity
        {
            var result = new List<T>();
            foreach (var id in (@this.Param(key) ?? "").Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries))
                if (await Context.Current.Database().GetOrDefault(id, typeof(T)) is T item) result.Add(item);
            return result;
        }

        public static Dictionary<string, string> GetRequestParameters(this ActionContext @this)
        {
            var request = @this.HttpContext.Request;
            var result = request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
            if (request.HasFormContentType) foreach (var f in request.Form) result[f.Key] = f.Value.ToString();
            foreach (var r in @this.RouteData.Values) result[r.Key] = r.Value?.ToString();
            return result;
        }

        public static object RouteValue(this IUrlHelper @this, string key) => @this.ActionContext.RouteData.Values[key];

        public static string Index<TController>(this IUrlHelper @this, object routeValues = null) where TController : Controller =>
            @this.Index(typeof(TController).Name.TrimEnd("Controller"), routeValues);

        public static string Index(this IUrlHelper @this, string controllerName, object routeValues = null) => @this.Action("Index", controllerName, routeValues);

        public static string Index(this IUrlHelper @this, string controllerName, Dictionary<string, string> routeParameters) =>
            @this.Action("Index", controllerName, new RouteValueDictionary(routeParameters.ToDictionary(p => p.Key, p => (object)p.Value)));

        public static string MergeRoute(this IUrlHelper @this, string routeTemplate, Dictionary<string, string> routeData)
        {
            var result = routeTemplate;
            foreach (var pair in routeData) result = result.Replace("{" + pair.Key + "}", pair.Value).Replace("{" + pair.Key + "?}", pair.Value);
            return System.Text.RegularExpressions.Regex.Replace(result, @"\{[^}]+\?\}", "").TrimEnd('/');
        }

        public static MenuItem BestMatchToCurrentUrl(this IEnumerable<MenuItem> @this) =>
            @this.FirstOrDefault(m => m.MatchesCurrentUrl()) ?? @this.FirstOrDefault(m => m.MatchesCurrentUrlWithoutQuery());

        public static HttpContext GetHttpContextBase(this HttpContext context) => context;

        public static bool IsAjaxGet(this HttpRequest request) => request.IsAjaxRequest() && request.IsGet();

        public static bool IsAjaxPost(this HttpRequest request) => request.IsAjaxRequest() && request.IsPost();

        public static UrlHelper GetUrlHelper(this HttpContext context) =>
            new UrlHelper(context.RequestServices.GetService<IActionContextAccessor>()?.ActionContext ?? new ActionContext(context, context.GetRouteData() ?? new RouteData(), new ActionDescriptor()));

        // Linux file systems are case sensitive; this lets /Scripts/x.js find wwwroot/scripts/x.js as it did on Windows.
        public static IApplicationBuilder UseStaticFilesCaseSensitive(this IApplicationBuilder app)
        {
            var env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value;
                if (!string.IsNullOrEmpty(path) && Path.HasExtension(path) && env.WebRootPath != null)
                {
                    var actual = FindCaseInsensitive(env.WebRootPath, path.TrimStart('/'));
                    if (actual != null) context.Request.Path = "/" + actual.Replace('\\', '/');
                }
                await next();
            });
            return app.UseStaticFiles();
        }

        static string FindCaseInsensitive(string root, string relative)
        {
            var current = root;
            var parts = new List<string>();
            foreach (var part in relative.Split('/'))
            {
                var match = Directory.Exists(current) ? Directory.EnumerateFileSystemEntries(current).Select(Path.GetFileName).FirstOrDefault(n => string.Equals(n, part, StringComparison.OrdinalIgnoreCase)) : null;
                if (match == null) return null;
                parts.Add(match);
                current = Path.Combine(current, match);
            }
            return string.Join("/", parts);
        }
    }

    public interface IViewRenderService
    {
        Task<string> RenderToStringAsync(string viewName, object model);
    }

    public class ViewRenderService : IViewRenderService
    {
        readonly IRazorViewEngine razorViewEngine;
        readonly ITempDataProvider tempDataProvider;
        readonly IServiceProvider serviceProvider;

        public ViewRenderService(IRazorViewEngine razorViewEngine, ITempDataProvider tempDataProvider, IServiceProvider serviceProvider)
        {
            this.razorViewEngine = razorViewEngine;
            this.tempDataProvider = tempDataProvider;
            this.serviceProvider = serviceProvider;
        }

        public async Task<string> RenderToStringAsync(string viewName, object model)
        {
            var http = serviceProvider.GetService<IHttpContextAccessor>()?.HttpContext ?? new DefaultHttpContext { RequestServices = serviceProvider };
            var actionContext = new ActionContext(http, http.GetRouteData() ?? new RouteData(), new ActionDescriptor());
            var view = razorViewEngine.FindView(actionContext, viewName, false).View ?? razorViewEngine.GetView(null, viewName, false).View
                ?? throw new ArgumentException($"View '{viewName}' was not found.");
            using (var writer = new StringWriter())
            {
                var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
                await view.RenderAsync(new ViewContext(actionContext, view, viewData, new TempDataDictionary(http, tempDataProvider), writer, new HtmlHelperOptions()));
                return writer.ToString();
            }
        }
    }

    /// <summary>Also looks for views under Views/Pages and Views/Modules, where M# put them.</summary>
    public class ViewLocationExpander : IViewLocationExpander
    {
        public virtual IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations) =>
            viewLocations.Concat(new[] { "/Views/Pages/{0}.cshtml", "/Views/Modules/{0}.cshtml", "/Views/Layouts/{0}.cshtml", "/Views/Shared/{0}.cshtml", "/Views/Modules/Components/{0}.cshtml" });

        public virtual void PopulateValues(ViewLocationExpanderContext context) { }
    }

    public abstract class RazorPage<TModel> : Microsoft.AspNetCore.Mvc.Razor.RazorPage<TModel>
    {
        protected virtual TModel info => Model;

        public HttpRequest Request => Context.Request;

        public static IDatabase Database => Platform.Context.Current.Database();

        public virtual IWebHostEnvironment Environment => Context.RequestServices.GetRequiredService<IWebHostEnvironment>();

        public FileInfo MapPath(string relativePath) => new FileInfo(System.IO.Path.Combine(Environment.ContentRootPath, relativePath.TrimStart('~', '/')));
    }

    public class EmptyListItem : SelectListItem
    {
        public EmptyListItem() : this("---Select---") { }

        public EmptyListItem(string text) : this(text, string.Empty) { }

        public EmptyListItem(string text, string value) : base(text, value) { }
    }

    [HtmlTargetElement("input", Attributes = "[asp-for],[type=hidden]")]
    public class HiddenTagHelper : TagHelper
    {
        [HtmlAttributeName("asp-for")]
        public ModelExpression For { get; set; }

        public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            if (For?.Model is IEntity entity) output.Attributes.SetAttribute("value", entity.GetId()?.ToString());
            return Task.CompletedTask;
        }
    }

    [HtmlTargetElement(Attributes = "asp-prefix")]
    public class PrefixIdentificationsTagHelper : TagHelper
    {
        [HtmlAttributeName("asp-prefix")]
        public string Prefix { get; set; }

        public override int Order => 0;

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            foreach (var name in new[] { "name", "id", "for" })
            {
                if (!output.Attributes.TryGetAttribute(name, out var attribute)) continue;
                output.Attributes.Remove(attribute);
                output.Attributes.Add(name, Prefix + "." + attribute.Value);
            }
        }
    }

    [HtmlTargetElement(Attributes = "asp-replace-this,asp-with-this")]
    public class ReplaceIdentificationsTagHelper : TagHelper
    {
        [HtmlAttributeName("asp-replace-this")]
        public string ReplaceThis { get; set; }

        [HtmlAttributeName("asp-with-this")]
        public string WithThis { get; set; }

        public override int Order => 0;

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            if (string.IsNullOrEmpty(ReplaceThis)) return;
            var content = (await output.GetChildContentAsync()).GetContent();
            output.Content.SetHtmlContent(content.Replace(ReplaceThis, WithThis ?? ""));
        }
    }

    [HtmlTargetElement("select", Attributes = "asp-for,asp-items")]
    public class SelectTagHelper : Microsoft.AspNetCore.Mvc.TagHelpers.SelectTagHelper
    {
        public SelectTagHelper(IHtmlGenerator generator) : base(generator) { }

        public override int Order => base.Order + 1;

        public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            // Entities bind by ID; give the base helper the ID so the right option is selected.
            if (For?.Model is IEntity entity && Items != null)
                foreach (var item in Items) item.Selected = item.Value == entity.GetId()?.ToString();
            return base.ProcessAsync(context, output);
        }
    }

    public interface IFileUploadMarkupGenerator
    {
        IHtmlContent Generate<TModel, TProperty>(IHtmlHelper html, object viewModel, Expression<Func<TModel, TProperty>> property, object htmlAttributes);
    }

    public class DefaultFileUploadMarkupGenerator : IFileUploadMarkupGenerator
    {
        public IHtmlContent Generate<TModel, TProperty>(IHtmlHelper html, object viewModel, Expression<Func<TModel, TProperty>> property, object htmlAttributes) =>
            DoGenerate(html, viewModel, property, htmlAttributes);

        protected virtual IHtmlContent DoGenerate<TModel, TProperty>(IHtmlHelper html, object viewModel, Expression<Func<TModel, TProperty>> property, object htmlAttributes)
        {
            var name = ((property.Body as MemberExpression)?.Member.Name) ?? "file";
            var blob = viewModel == null ? null : (property.Compile()((TModel)viewModel)) as BlobViewModel ?? new BlobViewModel();
            string Hidden(string field, object value) =>
                $"<input type=\"hidden\" id=\"{name}_{field}\" name=\"{name}_{field}\" class=\"{field}\" value=\"{value}\" />";
            using var validation = new System.IO.StringWriter();
            html.TextBox(name, "", "", new { tabindex = "-1", style = "width:1px; height:0; border:0; padding:0; margin:0;", @class = "validation", autocomplete = "off" }).WriteTo(validation, System.Text.Encodings.Web.HtmlEncoder.Default);
            return new HtmlString($@"
                <div class=""file-upload"">
                    <span class=""current-file"" aria-label=""Preview the file"">
                        <a target=""_blank"" href=""{blob.Url}"">{WebUtility.HtmlEncode(blob.Filename)}</a>
                    </span>
                    <label for=""{name}_fileInput"" hidden>HiddenLabel</label>
                    {validation}
                    <input type=""file"" id=""{name}_fileInput"" name=""files"" {GetHtmlAttributes(htmlAttributes)}/>
                    {Hidden("Action", blob.Action)}
                    {Hidden("TempFileId", blob.TempFileId)}
                    {Hidden("Filename", blob.Filename)}
                    {Hidden("ItemId", "")}
                    {Hidden("Url", blob.Url)}
                    {Hidden("IsEmpty", blob.IsEmpty)}
                    <div class=""progress-bar"" role=""progressbar""></div>
                    <span class=""delete-file fas fa-times btn"" style=""display: none""></span>
                </div>
");
        }

        protected virtual string GetHtmlAttributes(object htmlAttributes) => MvcExtensions.ToHtmlAttributes(htmlAttributes);
    }

    public interface IFileRequestService
    {
        Task DeleteTempFiles(TimeSpan olderThan);
        Task<Blob> Bind(string fileKey);
        Task<object> TempSaveUploadedFile(IFormFile file, bool allowUnsafeExtension = false);
        Task<object> CreateDownloadAction(byte[] data, string filename);
        Task<ActionResult> Download(string key);
    }

    /// <summary>Keeps uploads in a temp folder until the form is saved; each upload gets a folder named by a random key.</summary>
    public class DiskFileRequestService : IFileRequestService
    {
        static DirectoryInfo Root => new DirectoryInfo(Path.Combine(Path.GetTempPath(), "Platform.TempFiles"));

        static DirectoryInfo Folder(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Any(c => !char.IsLetterOrDigit(c) && c != '-')) throw new ArgumentException("Invalid file key.");
            return new DirectoryInfo(Path.Combine(Root.FullName, key));
        }

        public Task DeleteTempFiles(TimeSpan olderThan)
        {
            if (!Root.Exists) return Task.CompletedTask;
            foreach (var folder in Root.GetDirectories().Where(d => d.CreationTimeUtc < DateTime.UtcNow - olderThan))
                try { folder.Delete(recursive: true); } catch (IOException) { }
            return Task.CompletedTask;
        }

        public Task<Blob> Bind(string fileKey)
        {
            var file = Folder(fileKey).GetFiles().FirstOrDefault() ?? throw new ArgumentException("The uploaded file has expired. Please upload it again.");
            return Task.FromResult(new Blob(File.ReadAllBytes(file.FullName), file.Name));
        }

        public async Task<object> TempSaveUploadedFile(IFormFile file, bool allowUnsafeExtension = false)
        {
            if (!allowUnsafeExtension && Blob.HasUnsafeFileExtension(file.FileName)) return new { Error = "Invalid file extension." };
            var key = Guid.NewGuid().ToString();
            var folder = Folder(key);
            folder.Create();
            using (var stream = File.Create(Path.Combine(folder.FullName, Path.GetFileName(file.FileName)))) await file.CopyToAsync(stream);
            return new { Result = new { Filename = Path.GetFileName(file.FileName), TempFileId = key } };
        }

        public async Task<object> CreateDownloadAction(byte[] data, string filename)
        {
            var key = Guid.NewGuid().ToString();
            var folder = Folder(key);
            folder.Create();
            await Task.Run(() => File.WriteAllBytes(Path.Combine(folder.FullName, Path.GetFileName(filename)), data));
            return new { Download = "/temp-file/" + key };
        }

        public Task<ActionResult> Download(string key)
        {
            var file = Folder(key).GetFiles().FirstOrDefault();
            if (file == null) return Task.FromResult<ActionResult>(new NotFoundResult());
            return Task.FromResult<ActionResult>(new FileContentResult(File.ReadAllBytes(file.FullName), "application/octet-stream") { FileDownloadName = file.Name });
        }
    }

    public interface IFileAccessorFactory
    {
        Task<FileAccessor> Create(string path, IPrincipal currentUser);
    }

    public class FileAccessorFactory : IFileAccessorFactory
    {
        readonly Platform.Entities.Data.IDatabaseProviderConfig providerConfig;
        readonly IDatabase database;

        public FileAccessorFactory(Platform.Entities.Data.IDatabaseProviderConfig providerConfig, IDatabase database)
        {
            this.providerConfig = providerConfig;
            this.database = database;
        }

        public async Task<FileAccessor> Create(string path, IPrincipal currentUser)
        {
            var (type, property, id) = GetTypeAndProperty(path);
            var accessor = new FileAccessor(type, property, id, currentUser, database);
            await accessor.LoadBlob();
            return accessor;
        }

        // Path shape: {Type}.{Property}/{id}.{ext}
        protected virtual (Type Type, PropertyInfo PropertyInfo, string Id) GetTypeAndProperty(string path)
        {
            var parts = path.Trim('/').Split('/');
            if (parts.Length < 2) throw new ArgumentException("Invalid file path: " + path);
            var folder = parts[parts.Length - 2].Split('.');
            var type = EntityFinder.GetDomainEntityTypes().FirstOrDefault(t => t.Name == folder[0]) ?? throw new ArgumentException("Unknown type: " + folder[0]);
            var property = type.GetProperty(folder.Length > 1 ? folder[1] : "") ?? throw new ArgumentException("Unknown property: " + parts[parts.Length - 2]);
            return (type, property, Path.GetFileNameWithoutExtension(parts.Last()));
        }
    }

    public class FileAccessor
    {
        public readonly Type Type;
        public readonly PropertyInfo PropertyInfo;
        public string Id;
        public readonly IPrincipal CurrentUser;
        public readonly IDatabase Database;

        public FileAccessor(Type type, PropertyInfo propertyInfo, string id, IPrincipal user, IDatabase database)
        {
            Type = type;
            PropertyInfo = propertyInfo;
            Id = id;
            CurrentUser = user;
            Database = database;
        }

        public IEntity Instance { get; private set; }

        public Blob Blob { get; private set; }

        public string SecurityErrors { get; private set; }

        public async Task LoadBlob()
        {
            Instance = await Database.GetOrDefault(Id, Type);
            Blob = Instance == null ? null : PropertyInfo.GetValue(Instance) as Blob;
            if (Blob != null && Instance is Entity owner) Blob.Attach(owner, PropertyInfo.Name);
        }

        // [SecureFile] properties need an Is{Property}VisibleTo(IPrincipal) method on the entity.
        public bool IsAllowed()
        {
            if (!PropertyInfo.IsDefined(typeof(SecureFileAttribute), inherit: true)) return true;
            var check = Type.GetMethod("Is" + PropertyInfo.Name + "VisibleTo", new[] { typeof(IPrincipal) }) ?? Type.GetMethod("Is" + PropertyInfo.Name + "VisibleTo", new[] { typeof(ClaimsPrincipal) });
            if (check == null)
            {
                SecurityErrors = $"{Type.Name}.Is{PropertyInfo.Name}VisibleTo(IPrincipal) is not defined.";
                return false;
            }
            var allowed = check.Invoke(Instance, new object[] { CurrentUser });
            if (allowed is Task<bool> task) allowed = task.GetAwaiter().GetResult();
            if (allowed is true) return true;
            SecurityErrors = "You are not allowed to access this file.";
            return false;
        }
    }

    public abstract class RangeFileResult : ActionResult
    {
        protected RangeFileResult(string contentType, string fileName, DateTime modificationDate, long fileLength)
        {
            ContentType = contentType;
            FileName = fileName;
            FileModificationDate = modificationDate;
            FileLength = fileLength;
        }

        public string ContentType { get; }
        public string FileName { get; }
        public DateTime FileModificationDate { get; }
        public long FileLength { get; }

        protected virtual string GenerateEntityTag(ActionContext context) => "\"" + (FileName + FileModificationDate.Ticks + FileLength).CreateMD5Hash(asHex: true) + "\"";

        protected abstract void WriteEntireEntity(HttpResponse response);

        protected abstract void WriteEntityRange(HttpResponse response, long rangeStartIndex, long rangeEndIndex);

        public override void ExecuteResult(ActionContext context)
        {
            var request = context.HttpContext.Request;
            var response = context.HttpContext.Response;
            response.Headers["Accept-Ranges"] = "bytes";
            response.Headers["ETag"] = GenerateEntityTag(context);
            response.ContentType = ContentType;
            var range = request.Headers["Range"].ToString();
            if (range.StartsWith("bytes="))
            {
                var parts = range.Substring(6).Split('-');
                var start = long.TryParse(parts[0], out var s) ? s : 0;
                var end = parts.Length > 1 && long.TryParse(parts[1], out var e) ? e : FileLength - 1;
                response.StatusCode = 206;
                response.Headers["Content-Range"] = $"bytes {start}-{end}/{FileLength}";
                response.ContentLength = end - start + 1;
                WriteEntityRange(response, start, end);
                return;
            }
            response.ContentLength = FileLength;
            WriteEntireEntity(response);
        }

        public override Task ExecuteResultAsync(ActionContext context)
        {
            ExecuteResult(context);
            return Task.CompletedTask;
        }
    }

    public class RangeFileContentResult : RangeFileResult
    {
        public RangeFileContentResult(byte[] fileContents, string contentType, string fileName, DateTime modificationDate)
            : base(contentType, fileName, modificationDate, fileContents.Length) => FileContents = fileContents;

        public byte[] FileContents { get; }

        public static async Task<RangeFileContentResult> From(Blob blob) =>
            new RangeFileContentResult(await blob.GetFileDataAsync(), blob.GetMimeType(), blob.FileName, DateTime.UtcNow);

        protected override void WriteEntireEntity(HttpResponse response) => response.Body.WriteAsync(FileContents, 0, FileContents.Length).GetAwaiter().GetResult();

        protected override void WriteEntityRange(HttpResponse response, long rangeStartIndex, long rangeEndIndex) =>
            response.Body.WriteAsync(FileContents, (int)rangeStartIndex, (int)(rangeEndIndex - rangeStartIndex + 1)).GetAwaiter().GetResult();
    }
}
