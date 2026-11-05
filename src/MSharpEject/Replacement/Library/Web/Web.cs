using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Platform.Entities;

namespace Platform
{
    public static class WebExtensions
    {
        public static ApiClient AsHttpUser(this ApiClient @this)
        {
            var request = Context.Current.Request();
            var cookies = request.Cookies.Select(c => new Cookie(c.Key, c.Value)).ToArray();
            return @this.Authenticate(cookies);
        }

        public static ClaimsPrincipal User(this Context context) => context.Http()?.User;

        public static HttpContext Http(this Context context) => context.GetOptionalService<IHttpContextAccessor>()?.HttpContext;

        public static ActionContext ActionContext(this Context context) => context.GetOptionalService<IActionContextAccessor>()?.ActionContext;

        public static HttpRequest Request(this Context context) => context.Http()?.Request;

        public static HttpResponse Response(this Context context) => context.Http()?.Response;

        public static IWebHostEnvironment Environment(this Context context) => context.GetService<IWebHostEnvironment>();

        public static IEnumerable<KeyValuePair<string, string>> GetCookies(this HttpRequest @this) => @this.Cookies;

        public static async Task<T> GetOrDefault<T>(this HttpRequest @this, string key)
        {
            try { return await @this.Get<T>(key); }
            catch { return default(T); }
        }

        public static async Task<T> Get<T>(this HttpRequest @this, string key)
        {
            var value = @this.Param(key);
            if (typeof(IEntity).IsAssignableFrom(typeof(T)))
                return string.IsNullOrEmpty(value) ? default(T) : (T)await Context.Current.Database().Get(value, typeof(T));
            if (string.IsNullOrEmpty(value)) return default(T);
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (target == typeof(string)) return (T)(object)value;
            if (target.IsEnum) return (T)Enum.Parse(target, value, ignoreCase: true);
            if (target == typeof(Guid)) return (T)(object)Guid.Parse(value);
            return (T)Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string Param(this HttpRequest @this, string key)
        {
            if (@this.HasFormContentType && @this.Form.TryGetValue(key, out var form) && form.Count > 0) return form.ToString();
            if (@this.Query.TryGetValue(key, out var query) && query.Count > 0) return query.ToString();
            if (@this.RouteValues.TryGetValue(key, out var route) && route != null) return route.ToString();
            return null;
        }

        public static IEnumerable<Task<T>> GetList<T>(this HttpRequest @this, string key) where T : class, IEntity => @this.GetList<T>(key, '|');

        public static IEnumerable<Task<T>> GetList<T>(this HttpRequest @this, string key, char seperator) where T : class, IEntity =>
            (@this.Param(key) ?? "").Split(new[] { seperator }, StringSplitOptions.RemoveEmptyEntries)
                .Select(async id => (T)await Context.Current.Database().Get(id, typeof(T)));

        public static string FindGoogleSearchKeyword(this HttpRequest @this)
        {
            var referrer = @this.Headers["Referer"].ToString();
            if (!referrer.Contains("google.")) return null;
            var query = new Uri(referrer).Query.TrimStart('?').Split('&').FirstOrDefault(p => p.StartsWith("q="));
            return query == null ? null : WebUtility.UrlDecode(query.Substring(2));
        }

        public static string GetIPAddress(this HttpRequest @this) =>
            @this.Headers["X-Forwarded-For"].ToString().Split(',').FirstOrDefault()?.Trim().OrNullIfEmpty() ?? @this.HttpContext.Connection.RemoteIpAddress?.ToString();

        public static bool Has(this HttpRequest @this, string argument) => @this.Param(argument) != null;

        public static RouteValueDictionary GetRouteValues(this HttpRequest @this) => @this.RouteValues;

        public static bool Lacks(this HttpRequest @this, string argument) => !@this.Has(argument);

        public static string RootUrl(this HttpRequest @this) => $"{@this.Scheme}://{@this.Host}/";

        public static string ToRawUrl(this HttpRequest @this) => @this.PathBase + @this.Path + @this.QueryString;

        public static string ToPathAndQuery(this HttpRequest @this) => @this.Path + @this.QueryString;

        public static string ToEncodedString(this IQueryCollection @this) => string.Join("&", @this.SelectMany(p => p.Value.Select(v => WebUtility.UrlEncode(p.Key) + "=" + WebUtility.UrlEncode(v))));

        public static string ToAbsoluteUri(this HttpRequest @this) => @this.GetDisplayUrl();

        public static string GetAbsoluteUrl(this HttpRequest @this, string relativeUrl) => @this.RootUrl() + relativeUrl.TrimStart('/');

        public static bool IsAjaxRequest(this HttpRequest @this) => @this.Headers["X-Requested-With"] == "XMLHttpRequest";

        public static bool IsAjaxCall(this HttpRequest @this) => @this.IsAjaxRequest() || @this.Headers["Accept"].ToString().Contains("application/json");

        public static bool IsGet(this HttpRequest @this) => HttpMethods.IsGet(@this.Method);

        public static bool IsPost(this HttpRequest @this) => HttpMethods.IsPost(@this.Method);

        public static string GetReturnUrl(this HttpRequest @this)
        {
            var url = @this.Param("ReturnUrl");
            if (string.IsNullOrEmpty(url)) return string.Empty;
            // Only local paths, so a crafted link can't send the user to another site.
            return url.StartsWith("/") && !url.StartsWith("//") && !url.StartsWith("/\\") ? url : string.Empty;
        }

        public static bool IsLocal(this HttpRequest @this)
        {
            var connection = @this.HttpContext.Connection;
            if (connection.RemoteIpAddress == null) return true;
            return connection.LocalIpAddress != null ? connection.RemoteIpAddress.Equals(connection.LocalIpAddress) : IPAddress.IsLoopback(connection.RemoteIpAddress);
        }

        public static void RedirectPermanent(this HttpResponse response, string permanentUrl) => response.Redirect(permanentUrl, permanent: true);

        public static async Task Dispatch(this HttpResponse response, FileInfo responseFile, string fileName = null, string contentType = "Application/octet-stream") =>
            await response.Dispatch(File.ReadAllBytes(responseFile.FullName), fileName ?? responseFile.Name, contentType);

        public static async Task Dispatch(this HttpResponse response, Blob blob, string contentType = "Application/octet-stream") =>
            await response.Dispatch(await blob.GetFileDataAsync(), blob.FileName, contentType);

        public static async Task Dispatch(this HttpResponse response, byte[] responseData, string fileName, string contentType = "Application/octet-stream")
        {
            response.Clear();
            response.ContentType = contentType;
            response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName.Remove("\"")}\"";
            await response.Body.WriteAsync(responseData, 0, responseData.Length);
        }

        public static Task Dispatch(this HttpResponse response, string responseText, string fileName, string contentType = "Application/octet-stream", Encoding encoding = null) =>
            response.Dispatch((encoding ?? Encoding.UTF8).GetBytes(responseText), fileName, contentType);

        public static async Task EndWith(this HttpResponse response, string message, string mimeType = "text/html")
        {
            response.ContentType = mimeType;
            await response.WriteAsync(message);
            await response.CompleteAsync();
        }

        public static void Write(this HttpResponse response, string message) => response.Write(message, Encoding.UTF8);

        public static void Write(this HttpResponse response, string message, Encoding encoding) => response.WriteAsync(message, encoding).GetAwaiter().GetResult();

        public static void WriteLine(this HttpResponse response, string content) => response.Write(content + System.Environment.NewLine);
    }
}

namespace Platform.Mvc
{
    /// <summary>Adds an ETag from the response content and answers 304 when the browser already has it.</summary>
    public class SupportETagCacheAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuted(ActionExecutedContext context)
        {
            if (!(context.Result is ContentResult content) || content.Content == null) return;
            var etag = "\"" + content.Content.CreateMD5Hash(asHex: true) + "\"";
            var response = context.HttpContext.Response;
            response.Headers["ETag"] = etag;
            if (context.HttpContext.Request.Headers["If-None-Match"] == etag) context.Result = new StatusCodeResult(304);
        }
    }
}

namespace Platform.Web
{
    /// <summary>Strongly typed values kept in browser cookies, keyed by the value's type name unless named.</summary>
    public class CookieProperty
    {
        static HttpContext Http => Context.Current.Http();

        static string Key<T>() => typeof(T).FullName;

        public static Task<T> Get<T>() => Get<T>(Key<T>(), default(T));

        public static Task<string> Get(string key) => Task.FromResult(Http?.Request.Cookies[key]);

        public static Task<T> Get<T>(T defaultValue) => Get(Key<T>(), defaultValue);

        public static Task<T> Get<T>(string propertyName) => Get(propertyName, default(T));

        public static async Task<IEnumerable<string>> GetStrings(string propertyName) =>
            ((await Get(propertyName)) ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(WebUtility.UrlDecode).ToArray();

        public static async Task<T> Get<T>(string propertyName, T defaultValue)
        {
            var value = await Get(propertyName);
            if (string.IsNullOrEmpty(value)) return defaultValue;
            if (typeof(IEntity).IsAssignableFrom(typeof(T))) return (T)await Context.Current.Database().GetOrDefault(value, typeof(T));
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (target == typeof(string)) return (T)(object)value;
            if (target.IsEnum) return (T)Enum.Parse(target, value);
            if (target == typeof(Guid)) return (T)(object)Guid.Parse(value);
            return (T)Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static void Set<T>(T value, bool isHttpOnly = true) => Set(Key<T>(), value, isHttpOnly);

        public static void Set<T>(string propertyName, T value, bool isHttpOnly = true) =>
            Set(propertyName, value is IEntity entity ? entity.GetId()?.ToString() : value?.ToString(), isHttpOnly);

        public static void SetList<T>(string propertyName, IEnumerable<T> list, bool isHttpOnly = true) where T : IEntity =>
            Set(propertyName, list.Select(i => i.GetId()?.ToString()), isHttpOnly);

        public static Task<IEnumerable<T>> GetList<T>() where T : IEntity => GetList<T>(Key<T>()).ContinueWith(t => (IEnumerable<T>)t.Result);

        public static async Task<T[]> GetList<T>(string propertyName) where T : IEntity
        {
            var result = new List<T>();
            foreach (var id in await GetStrings(propertyName))
                if (await Context.Current.Database().GetOrDefault(id, typeof(T)) is T item) result.Add(item);
            return result.ToArray();
        }

        public static void Remove<T>() => Remove(Key<T>());

        public static void Remove<T>(string propertyName) => Remove(propertyName);

        public static void Remove(string propertyName) => Http?.Response.Cookies.Delete(propertyName);

        public static void Set(string propertyName, IEnumerable<string> strings, bool isHttpOnly = true) =>
            Set(propertyName, string.Join("|", strings.Select(WebUtility.UrlEncode)), isHttpOnly);

        public static void Set(string key, string value, bool isHttpOnly = true) => Set(key, value, DateTimeOffset.Now.AddYears(10), isHttpOnly);

        public static void Set(string key, string value, DateTimeOffset expires, bool isHttpOnly = true)
        {
            if (value == null)
            {
                Remove(key);
                return;
            }
            Http?.Response.Cookies.Append(key, value, new CookieOptions { Expires = expires, HttpOnly = isHttpOnly, Secure = Http.Request.IsHttps, SameSite = SameSiteMode.Lax });
        }
    }

    /// <summary>Values cached for the current request only.</summary>
    public class HttpContextCache
    {
        static IDictionary<object, object> Items => Context.Current.Http()?.Items;

        public static TValue GetOrAdd<TKey, TValue>(TKey key, Func<TValue> valueProducer)
        {
            var items = Items;
            if (items == null) return valueProducer();
            var cacheKey = (typeof(HttpContextCache), key);
            if (items.TryGetValue(cacheKey, out var value)) return (TValue)value;
            var result = valueProducer();
            items[cacheKey] = result;
            return result;
        }

        public static void Remove<TKey>(TKey key) => Items?.Remove((typeof(HttpContextCache), key));
    }
}
