using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Platform
{
    public enum ApiFallBackEventPolicy { Silent = 0, Raise = 1 }

    public enum OnApiCallError { Throw = 0, Ignore = 1, IgnoreAndNotify = 2 }

    public enum CachePolicy
    {
        FreshOrFail = 0, FreshOrNull = 1, FreshOrCacheOrNull = 2, FreshOrCacheOrFail = 3,
        CacheOrFreshOrFail = 4, CacheOrFreshOrNull = 5, CacheOrNull = 6, CacheOrFail = 7,
    }

    public static class CachePolicyExtention
    {
        internal static bool CacheFirst(this CachePolicy p) => p >= CachePolicy.CacheOrFreshOrFail;
        internal static bool CacheOnly(this CachePolicy p) => p == CachePolicy.CacheOrNull || p == CachePolicy.CacheOrFail;
        internal static bool UsesCache(this CachePolicy p) => p != CachePolicy.FreshOrFail && p != CachePolicy.FreshOrNull;
        internal static bool Fails(this CachePolicy p) => p == CachePolicy.FreshOrFail || p == CachePolicy.FreshOrCacheOrFail || p == CachePolicy.CacheOrFreshOrFail || p == CachePolicy.CacheOrFail;
    }

    public class FallBackEvent
    {
        public string FriendlyMessage { get; set; }
        public string ExceptionMessage { get; set; }
        public TimeSpan? CacheAge { get; set; }
        public string Url { get; set; }
    }

    public class ApiResponseCache
    {
        public static object CacheSyncLock = new object();

        public string Message { get; internal set; }
        public string Url { get; internal set; }
        public string OriginalErrorMessage { get; internal set; }
        public FileInfo File { get; internal set; }
        public DateTime CreationDate => File?.Exists == true ? File.LastWriteTimeUtc : DateTime.UtcNow;
        public TimeSpan Age => DateTime.UtcNow - CreationDate;

        public static DirectoryInfo GetRootCacheDirectory() =>
            new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "App_Data", "Api-Cache"));

        public static string GetTypeName<T>() => typeof(T).FullName.Replace("`", "_").Replace("[", "_").Replace("]", "_").Replace(",", "_");

        internal static FileInfo FileFor<T>(string url) =>
            new FileInfo(Path.Combine(GetRootCacheDirectory().FullName, GetTypeName<T>(), url.CreateMD5Hash(asHex: true) + ".json"));
    }

    public class ApiResponseCache<TData> : ApiResponseCache
    {
        public TData Data { get; internal set; }

        internal static ApiResponseCache<TData> Read(string url)
        {
            var file = FileFor<TData>(url);
            lock (CacheSyncLock)
            {
                if (!file.Exists) return null;
                return new ApiResponseCache<TData> { Url = url, File = file, Data = JsonConvert.DeserializeObject<TData>(System.IO.File.ReadAllText(file.FullName)) };
            }
        }

        internal static void Write(string url, TData data)
        {
            var file = FileFor<TData>(url);
            lock (CacheSyncLock)
            {
                file.Directory.Create();
                System.IO.File.WriteAllText(file.FullName, JsonConvert.SerializeObject(data));
            }
        }
    }

    public class ApiClient
    {
        static readonly ConcurrentDictionary<string, (int Failures, DateTime OpenUntil)> Breakers = new ConcurrentDictionary<string, (int, DateTime)>();

        public readonly IHttpClientFactory Factory;
        readonly List<Cookie> cookies = new List<Cookie>();
        readonly List<Action<HttpRequestHeaders>> headers = new List<Action<HttpRequestHeaders>>();
        CachePolicy cachePolicy = CachePolicy.FreshOrFail;
        TimeSpan? cacheExpiry;
        int retries, retryPause = 100, breakAfter, breakSeconds;

        public static event AwaitableEventHandler<FallBackEvent> FallBack;

        public ApiClient(string apiUrl)
        {
            Url = apiUrl;
            try { Factory = Context.Current.GetOptionalService<IHttpClientFactory>(); }
            catch (InvalidOperationException) { }
        }

        public string Url { get; private set; }

        public ApiFallBackEventPolicy FallBackEventPolicy { get; private set; } = ApiFallBackEventPolicy.Raise;

        public bool EnsureTrailingSlash { get; set; } = true;

        public ApiClient Authenticate(params Cookie[] identityCookies)
        {
            cookies.AddRange(identityCookies);
            return this;
        }

        public ApiClient OnFallBack(ApiFallBackEventPolicy fallBackEventPolicy)
        {
            FallBackEventPolicy = fallBackEventPolicy;
            return this;
        }

        public ApiClient Cache(CachePolicy policy, TimeSpan? cacheExpiry = null)
        {
            cachePolicy = policy;
            this.cacheExpiry = cacheExpiry;
            return this;
        }

        public ApiClient Header(Action<HttpRequestHeaders> config)
        {
            headers.Add(config);
            return this;
        }

        public ApiClient Retries(int retries, int pauseMilliseconds = 100)
        {
            this.retries = retries;
            retryPause = pauseMilliseconds;
            return this;
        }

        public ApiClient CircuitBreaker(int exceptionsBeforeBreaking = 5, int breakDurationSeconds = 10)
        {
            breakAfter = exceptionsBeforeBreaking;
            breakSeconds = breakDurationSeconds;
            return this;
        }

        public static Task DisposeCache()
        {
            lock (ApiResponseCache.CacheSyncLock)
            {
                var root = ApiResponseCache.GetRootCacheDirectory();
                if (root.Exists) root.Delete(recursive: true);
            }
            return Task.CompletedTask;
        }

        public Task DisposeCache<TResponse>(string getApiUrl)
        {
            lock (ApiResponseCache.CacheSyncLock)
            {
                var file = ApiResponseCache.FileFor<TResponse>(getApiUrl);
                if (file.Exists) file.Delete();
            }
            return Task.CompletedTask;
        }

        string RequestUrl(object queryParams = null)
        {
            var url = Url;
            if (EnsureTrailingSlash && !url.Contains("?") && !url.EndsWith("/") && Path.GetExtension(new Uri(url, UriKind.RelativeOrAbsolute).IsAbsoluteUri ? new Uri(url).AbsolutePath : url).Length == 0) url += "/";
            if (queryParams == null) return url;
            var query = queryParams is string s ? s : string.Join("&", queryParams.GetType().GetProperties()
                .Select(p => (p.Name, Value: p.GetValue(queryParams))).Where(p => p.Value != null)
                .Select(p => Uri.EscapeDataString(p.Name) + "=" + Uri.EscapeDataString(p.Value.ToString())));
            return query.Length == 0 ? url : url + (url.Contains("?") ? "&" : "?") + query;
        }

        public async Task<TResponse> Get<TResponse>(object queryParams = null)
        {
            var url = RequestUrl(queryParams);
            var cached = cachePolicy.UsesCache() ? ApiResponseCache<TResponse>.Read(url) : null;
            if (cached != null && cacheExpiry.HasValue && cached.Age > cacheExpiry.Value) cached = null;

            if (cachePolicy.CacheOnly())
            {
                if (cached != null) return cached.Data;
                if (cachePolicy.Fails()) throw new InvalidOperationException("No cached response for " + url);
                return default(TResponse);
            }
            if (cachePolicy.CacheFirst() && cached != null) return cached.Data;

            var request = new RequestInfo(this) { HttpMethod = "GET", Url = url };
            try
            {
                var result = await request.TrySend<TResponse>();
                if (cachePolicy.UsesCache()) ApiResponseCache<TResponse>.Write(url, result);
                return result;
            }
            catch (Exception ex)
            {
                if (cached != null && cachePolicy != CachePolicy.FreshOrFail && cachePolicy != CachePolicy.FreshOrNull)
                {
                    await RaiseFallBack(url, ex, cached.Age);
                    return cached.Data;
                }
                await RaiseFallBack(url, ex, null);
                if (cachePolicy.Fails()) throw;
                return default(TResponse);
            }
        }

        Task RaiseFallBack(string url, Exception ex, TimeSpan? age) =>
            FallBackEventPolicy == ApiFallBackEventPolicy.Raise
                ? FallBack.Raise(new FallBackEvent { Url = url, ExceptionMessage = ex.Message, CacheAge = age, FriendlyMessage = age == null ? "Failed to get fresh results from " + url : $"Failed to get fresh results from {url}. Using the latest cache from {age.Value.ToNaturalTime(1)} ago." })
                : Task.CompletedTask;

        async Task<TResponse> Send<TResponse>(string method, object jsonParams, string requestData)
        {
            var request = new RequestInfo(this) { HttpMethod = method, Url = RequestUrl(), JsonData = jsonParams, RequestData = requestData };
            return await request.TrySend<TResponse>();
        }

        async Task<bool> SendOk(string method, object jsonParams, string requestData)
        {
            await Send<string>(method, jsonParams, requestData);
            return true;
        }

        public Task<bool> Delete(object jsonParams = null) => SendOk("DELETE", jsonParams, null);
        public Task<TResponse> Delete<TResponse>(object jsonParams) => Send<TResponse>("DELETE", jsonParams, null);
        public Task<bool> Patch() => SendOk("PATCH", null, null);
        public Task<bool> Patch(string requestData) => SendOk("PATCH", null, requestData);
        public Task<bool> Patch(object jsonParams) => SendOk("PATCH", jsonParams, null);
        public Task<TResponse> Patch<TResponse>(string requestData) => Send<TResponse>("PATCH", null, requestData);
        public Task<TResponse> Patch<TResponse>(object jsonParams) => Send<TResponse>("PATCH", jsonParams, null);
        public Task<bool> Post() => SendOk("POST", null, null);
        public Task<bool> Post(string requestData) => SendOk("POST", null, requestData);
        public Task<bool> Post(object jsonParams) => SendOk("POST", jsonParams, null);
        public Task<TResponse> Post<TResponse>(string requestData) => Send<TResponse>("POST", null, requestData);
        public Task<TResponse> Post<TResponse>(object jsonParams) => Send<TResponse>("POST", jsonParams, null);
        public Task<bool> Put() => SendOk("PUT", null, null);
        public Task<bool> Put(string requestData) => SendOk("PUT", null, requestData);
        public Task<bool> Put(object jsonParams) => SendOk("PUT", jsonParams, null);
        public Task<TResponse> Put<TResponse>(string requestData) => Send<TResponse>("PUT", null, requestData);
        public Task<TResponse> Put<TResponse>(object jsonParams) => Send<TResponse>("PUT", jsonParams, null);

        HttpClient CreateClient()
        {
            if (cookies.Count == 0 && Factory != null) return Factory.CreateClient();
            var container = new CookieContainer();
            var uri = new Uri(Url);
            foreach (var cookie in cookies) container.Add(uri, new Cookie(cookie.Name, cookie.Value));
            return new HttpClient(new HttpClientHandler { CookieContainer = container });
        }

        public class RequestInfo
        {
            readonly ApiClient client;

            public RequestInfo(ApiClient client) => this.client = client;

            internal string Url { get; set; }
            public string HttpMethod { get; set; } = "GET";
            public string ContentType { get; set; }
            public string RequestData { get; set; }
            public string ResponseText { get; set; }
            public HttpStatusCode ResponseCode { get; private set; }
            public HttpResponseHeaders ResponseHeaders { get; private set; }
            public Exception Error { get; private set; }
            public object JsonData { get; set; }

            public string GetContentType() => ContentType ?? (JsonData != null ? "application/json" : "application/x-www-form-urlencoded");

            public async Task<TResponse> TrySend<TResponse>()
            {
                var key = new Uri(Url ?? client.Url).GetLeftPart(UriPartial.Authority);
                if (client.breakAfter > 0 && Breakers.TryGetValue(key, out var state) && state.OpenUntil > DateTime.UtcNow)
                    throw new HttpRequestException($"The circuit is open for {key} until {state.OpenUntil:u}.");

                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        await Send();
                        if (client.breakAfter > 0) Breakers.TryRemove(key, out _);
                        return ExtractResponse<TResponse>();
                    }
                    catch (Exception ex) when (attempt < client.retries)
                    {
                        Error = ex;
                        await Task.Delay(client.retryPause);
                    }
                    catch (Exception ex)
                    {
                        Error = ex;
                        if (client.breakAfter > 0)
                        {
                            var updated = Breakers.AddOrUpdate(key, _ => (1, DateTime.MinValue), (_, s) => (s.Failures + 1, s.OpenUntil));
                            if (updated.Failures >= client.breakAfter) Breakers[key] = (0, DateTime.UtcNow.AddSeconds(client.breakSeconds));
                        }
                        throw;
                    }
                }
            }

            async Task Send()
            {
                using (var http = client.CreateClient())
                using (var request = new HttpRequestMessage(new HttpMethod(HttpMethod), Url ?? client.Url))
                {
                    foreach (var config in client.headers) config(request.Headers);
                    var body = JsonData != null ? JsonConvert.SerializeObject(JsonData) : RequestData;
                    if (body != null) request.Content = new StringContent(body, Encoding.UTF8, GetContentType());
                    using (var response = await http.SendAsync(request))
                    {
                        ResponseCode = response.StatusCode;
                        ResponseHeaders = response.Headers;
                        ResponseText = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException($"{HttpMethod} {Url ?? client.Url} returned {(int)response.StatusCode} {response.StatusCode}: {ResponseText}");
                    }
                }
            }

            public TResponse ExtractResponse<TResponse>()
            {
                if (typeof(TResponse) == typeof(string)) return (TResponse)(object)ResponseText;
                if (string.IsNullOrWhiteSpace(ResponseText)) return default(TResponse);
                return JsonConvert.DeserializeObject<TResponse>(ResponseText);
            }
        }
    }

    public class StronglyTypedApiProxy
    {
        public List<Action<ApiClient>> Configurators = new List<Action<ApiClient>>();

        public StronglyTypedApiProxy Configure(Action<ApiClient> config)
        {
            Configurators.Add(config);
            return this;
        }

        protected ApiClient Client(string url)
        {
            var client = new ApiClient(url);
            foreach (var configure in Configurators) configure(client);
            return client;
        }

        public override string ToString() => GetType().FullName;

        public override int GetHashCode() => GetType().GetHashCode();

        public override bool Equals(object obj) => obj?.GetType() == GetType();
    }

    public static class ApiClientExtensions
    {
        public static DevCommandsOptions AddClearApiCache(this DevCommandsOptions @this) => @this;
    }
}
