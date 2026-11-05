using System;

namespace Platform
{
    /// <summary>A named service whose base URL comes from Config "Microservice:{name}:Url".</summary>
    public class Microservice
    {
        Microservice(string name) => Name = name;

        public string Name { get; }

        public static Microservice Of(string serviceName) => new Microservice(serviceName);

        public static Microservice Me => Of(Config.Get("Microservice:Me:Name"));

        public string Url(string relativeUrl = null)
        {
            var root = Config.Get($"Microservice:{Name}:Url") ?? throw new InvalidOperationException($"Could not find the url to the '{Name}' microservice (Microservice:{Name}:Url).");
            if (string.IsNullOrEmpty(relativeUrl)) return root;
            return root.TrimEnd('/') + "/" + relativeUrl.TrimStart('/');
        }

        public ApiClient Api(string relativeApiUrl) => new ApiClient(Url(relativeApiUrl));
    }

    public static class MicroserviceExtensions
    {
        public static T Use<T>(this T @this, Action<ApiClient> config) where T : StronglyTypedApiProxy
        {
            @this.Configure(config);
            return @this;
        }

        public static T Cache<T>(this T proxy, CachePolicy policy, TimeSpan? cacheExpiry = null) where T : StronglyTypedApiProxy => proxy.Use(c => c.Cache(policy, cacheExpiry));

        public static T CircuitBreaker<T>(this T proxy, int exceptionsBeforeBreaking = 5, int breakDurationSeconds = 10) where T : StronglyTypedApiProxy =>
            proxy.Use(c => c.CircuitBreaker(exceptionsBeforeBreaking, breakDurationSeconds));

        public static T Retries<T>(this T proxy, int retries, int pauseMilliseconds = 100) where T : StronglyTypedApiProxy => proxy.Use(c => c.Retries(retries, pauseMilliseconds));
    }
}
