using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Entities;
using Platform.Entities.Data;

namespace Platform.Mvc
{
    /// <summary>The app's Startup base: MVC with the entity binders, cookie auth, localisation, the database, and conventional plus attribute routes.</summary>
    public abstract class Startup
    {
        protected readonly IWebHostEnvironment Environment;
        protected readonly IConfiguration Configuration;

        protected Startup(IWebHostEnvironment env, IConfiguration config, ILoggerFactory loggerFactory)
        {
            Environment = env;
            Configuration = config;
            Platform.Config.SetConfiguration(config);
            Log.Init(loggerFactory);
        }

        protected IServiceCollection Services { get; private set; }

        protected virtual RequestLocalizationOptions RequestLocalizationOptions
        {
            get
            {
                var culture = GetRequestCulture();
                return new RequestLocalizationOptions
                {
                    DefaultRequestCulture = new RequestCulture(culture),
                    SupportedCultures = new List<CultureInfo> { culture },
                    SupportedUICultures = new List<CultureInfo> { culture },
                };
            }
        }

        public virtual void ConfigureServices(IServiceCollection services)
        {
            Services = services;
            services.AddHttpContextAccessor();
            services.AddSingleton<IActionContextAccessor, ActionContextAccessor>();
            services.AddTransient<IFileRequestService, DiskFileRequestService>();
            services.AddTransient<IFileAccessorFactory, FileAccessorFactory>();
            services.AddTransient<IFileUploadMarkupGenerator, DefaultFileUploadMarkupGenerator>();
            services.AddSingleton(Configuration);
            services.AddDatabase(Configuration);
            if (services.All(s => s.ServiceType != typeof(Platform.Audit.IAudit))) services.AddDefaultAudit();
            services.AddHttpClient();
            services.AddCors(options =>
            {
                var allow = Configuration["cors:allow"];
                if (string.IsNullOrEmpty(allow)) return;
                options.AddPolicy("AllowedByConfig", policy => policy
                    .WithOrigins(allow.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(o => o.Trim()).ToArray())
                    .SetIsOriginAllowedToAllowWildcardSubdomains().AllowAnyMethod().AllowAnyHeader().AllowCredentials());
            });
            services.AddResponseCompression();

            MvcBuilder = services.AddMvc();
            services.AddSingleton<Microsoft.AspNetCore.Mvc.DataAnnotations.IValidationAttributeAdapterProvider, OliveValidationAttributeAdapterProvider>();
            ConfigureMvc(MvcBuilder);

            ConfigureAuthentication(services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(ConfigureAuthCookie));
        }

        internal static IMvcBuilder MvcBuilder;

        // Pascal case, as the front end and view model JSON expect.
        protected virtual void ConfigureJsonOptions(JsonOptions options) => options.JsonSerializerOptions.PropertyNamingPolicy = null;

        protected virtual void ConfigureAuthentication(AuthenticationBuilder auth) { }

        // Apps that override without calling base lose these, as with Olive.
        protected virtual void ConfigureMvc(IMvcBuilder mvc) => mvc
            .AddMvcOptions(options =>
            {
                options.EnableEndpointRouting = false;
                options.ModelBinderProviders.Insert(0, new OliveBinderProvider());
            })
            .AddJsonOptions(ConfigureJsonOptions)
            .AddRazorOptions(o => o.ViewLocationExpanders.Add(new ViewLocationExpander()));

        protected virtual void ConfigureAuthCookie(CookieAuthenticationOptions options)
        {
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/login";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
        }

        public virtual void Configure(IApplicationBuilder app)
        {
            Context.Initialize(app.ApplicationServices, () => app.ApplicationServices.GetService<IHttpContextAccessor>()?.HttpContext?.RequestServices);
            app.UseLogUnhandledExceptionsMiddleware();
            ConfigureRequestHandlers(app);
        }

        public virtual async Task OnStartUpAsync(IApplicationBuilder app)
        {
            app.ApplicationServices.GetService<IDatabaseProviderConfig>()?.Configure();
            await Context.OnStartedUp();
        }

        protected virtual void ConfigureSecurity(IApplicationBuilder app)
        {
            app.UseCookiePolicy();
            ConfigureMicroserviceSecurity(app);
            app.UseAuthentication();
            app.UseMiddleware<SplitRoleClaimsMiddleware>();
        }

        protected virtual void ConfigureMicroserviceSecurity(IApplicationBuilder app) => app.UseMicroserviceAccessKeyAuthentication();

        // Olive maps attribute routes only and never calls ConfigureRoutes.
        protected virtual void ConfigureRequestHandlers(IApplicationBuilder app)
        {
            app.UseResponseCompression();
            UseStaticFiles(app);
            ConfigureExceptionPage(app);
            ConfigureSecurity(app);
            ConfigureMiddlewares(app);
            app.UseRequestLocalization(RequestLocalizationOptions);
            app.UseMvc();
        }

        // OnStartUpAsync runs once, on the first request.
        // ponytail: Olive's request timing log is skipped, add when someone reads it.
        protected virtual void ConfigureMiddlewares(IApplicationBuilder app)
        {
            var startup = new Lazy<Task>(() => OnStartUpAsync(app));
            app.Use(async (context, next) =>
            {
                await startup.Value;
                await next();
            });
        }

        protected virtual void UseStaticFiles(IApplicationBuilder app) => app.UseStaticFilesCaseSensitive();

        protected virtual void ConfigureExceptionPage(IApplicationBuilder app)
        {
            if (Environment.IsDevelopment()) app.UseDeveloperExceptionPage();
            else app.UseExceptionHandler("/error");
        }

        protected virtual CultureInfo GetRequestCulture() => CultureInfo.CurrentCulture;

        protected virtual void ConfigureRoutes(IRouteBuilder routes) { }
    }

    public abstract class BaseMiddleware
    {
        public readonly RequestDelegate Next;

        public BaseMiddleware(RequestDelegate next) => Next = next;

        public abstract Task Invoke(HttpContext context);
    }

    /// <summary>Splits a comma separated role claim into one claim per role.</summary>
    public class SplitRoleClaimsMiddleware : BaseMiddleware
    {
        public SplitRoleClaimsMiddleware(RequestDelegate next) : base(next) { }

        public override Task Invoke(HttpContext context)
        {
            if (context.User?.Identity is ClaimsIdentity identity)
                foreach (var claim in identity.FindAll(ClaimTypes.Role).Where(c => c.Value.Contains(",")).ToList())
                {
                    identity.RemoveClaim(claim);
                    foreach (var role in claim.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) identity.AddClaim(new Claim(ClaimTypes.Role, role.Trim()));
                }
            return Next(context);
        }
    }

    /// <summary>Runs dev commands at /cmd/{name} outside production.</summary>
    public class DevCommandMiddleware
    {
        readonly IServiceProvider serviceProvider;
        readonly RequestDelegate next;

        public DevCommandMiddleware(IServiceProvider serviceProvider, RequestDelegate next)
        {
            this.serviceProvider = serviceProvider;
            this.next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";
            if (!path.StartsWith("/cmd/", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }
            var name = path.Substring(5);
            var command = serviceProvider.GetServices<IDevCommand>().FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && c.IsEnabled());
            if (command == null)
            {
                context.Response.StatusCode = 404;
                return;
            }
            var redirect = await command.Run();
            if (!string.IsNullOrEmpty(redirect)) context.Response.Redirect(redirect);
        }
    }

    public static class HandleExceptionsMiddlewareExtensions
    {
        public static IApplicationBuilder UseLogUnhandledExceptionsMiddleware(this IApplicationBuilder app) => app.Use(async (context, next) =>
        {
            try { await next(); }
            catch (Exception ex)
            {
                Log.For(typeof(HandleExceptionsMiddlewareExtensions)).LogError(ex, "Unhandled exception on " + context.Request.Path);
                throw;
            }
        });
    }

    public static class PerformanceMonitoringMiddlewareExtensions
    {
        internal static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static IApplicationBuilder IgnorePerformance(this IApplicationBuilder app, string url)
        {
            Ignored.Add(url);
            return app;
        }
    }

    public static class SameSiteCookiesServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureNonBreakingSameSiteCookies(this IServiceCollection services) =>
            services.Configure<CookiePolicyOptions>(options =>
            {
                options.MinimumSameSitePolicy = SameSiteMode.Unspecified;
                options.OnAppendCookie = c => Adjust(c.Context, c.CookieOptions);
                options.OnDeleteCookie = c => Adjust(c.Context, c.CookieOptions);
            });

        // Old Safari and Chrome 51-66 reject SameSite=None; drop the attribute for them.
        static void Adjust(HttpContext http, CookieOptions options)
        {
            if (options.SameSite != SameSiteMode.None) return;
            var agent = http.Request.Headers["User-Agent"].ToString();
            if (agent.Contains("CPU iPhone OS 12") || agent.Contains("iPad; CPU OS 12") || (agent.Contains("Macintosh; Intel Mac OS X 10_14") && agent.Contains("Version/") && agent.Contains("Safari"))
                || agent.Contains("Chrome/5") || agent.Contains("Chrome/6"))
                options.SameSite = SameSiteMode.Unspecified;
        }
    }

    public static class PropertyEncryptionExtensions
    {
        public static void UsePropertyEncryption(this IApplicationBuilder @this, System.Reflection.Assembly domainAssembly) =>
            throw new NotSupportedException("Encrypted entity properties aren't supported by the replacement library: the stored cipher format must match Olive's.");

        public static void AddEntityInterceptor(this IServiceCollection @this) =>
            throw new NotSupportedException("Encrypted entity properties aren't supported by the replacement library: the stored cipher format must match Olive's.");

        public static void AddEntityInterceptor<SavingInterceptor, LoadedInterceptor>(this IServiceCollection @this)
            where SavingInterceptor : class, IEntitySavingInterceptor where LoadedInterceptor : class, IEntityLoadedInterceptor
        {
            @this.AddSingleton<IEntitySavingInterceptor, SavingInterceptor>();
            @this.AddSingleton<IEntityLoadedInterceptor, LoadedInterceptor>();
        }
    }

    public static class MicroserviceAccessKeyAuthentication
    {
        public static IApplicationBuilder UseMicroserviceAccessKeyAuthentication(this IApplicationBuilder app) => app.Use(async (context, next) =>
        {
            var key = context.Request.Headers["Microservice.AccessKey"].ToString();
            if (!string.IsNullOrEmpty(key))
            {
                var clientId = Config.Get($"Authentication:MicroserviceAccessKeys:{key}");
                if (clientId != null)
                {
                    var roles = context.RequestServices.GetService<PermissionsProvider>()?.GetRoles(clientId) ?? Enumerable.Empty<string>();
                    var claims = new List<Claim> { new Claim(ClaimTypes.Name, clientId) }.Concat(roles.Select(r => new Claim(ClaimTypes.Role, r)));
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Microservice.AccessKey"));
                }
            }
            await next();
        });

        public abstract class PermissionsProvider
        {
            public abstract IEnumerable<string> GetRoles(string clientId);
        }
    }

    public class AuthorizeApiAttribute : TypeFilterAttribute
    {
        public AuthorizeApiAttribute(string roles = null) : base(typeof(ClaimRequirementFilter))
        {
            Roles = roles;
            Arguments = new object[] { roles ?? string.Empty };
        }

        public string Roles { get; set; }
    }

    public class ClaimRequirementFilter : IAuthorizationFilter
    {
        readonly string roles;

        public ClaimRequirementFilter(string roles) => this.roles = roles;

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                context.Result = new UnauthorizedResult();
                return;
            }
            var required = (roles ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).ToArray();
            if (required.Length > 0 && !required.Any(user.IsInRole)) context.Result = new ForbidResult();
        }
    }

    public class JsonHandlerAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuted(ActionExecutedContext filterContext)
        {
            if (filterContext.Result is ObjectResult objectResult) filterContext.Result = new JsonNetResult(objectResult.Value);
        }
    }

    public class ReturnsAttribute : ProducesResponseTypeAttribute
    {
        public ReturnsAttribute(Type returnType) : base(returnType, 200) { }
    }

    public interface ITaskManager
    {
        void Run();
    }

    public interface ITempDatabase
    {
        Task Restart();
        Task AwaitReadiness();
        Task ReCreateDb();
        Task Seed();
    }
}
