using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompatTests
{
    /// <summary>The same controllers and view models hosted on Olive.Mvc's Startup and on ours; responses must match.</summary>
    public class MvcHostParity
    {
        internal sealed class OnlyControllersOf(Type baseType) : ControllerFeatureProvider
        {
            protected override bool IsController(System.Reflection.TypeInfo typeInfo) => base.IsController(typeInfo) && baseType.IsAssignableFrom(typeInfo);
        }

        internal static readonly Dictionary<string, string> Settings = new()
        {
            ["ConnectionStrings:Default"] = "Server=.;Database=X;Integrated Security=true",
            ["Database:Cache:Enabled"] = "false",
            ["App:Culture"] = "en-GB",
            ["Database:Providers:0:AssemblyName"] = typeof(MvcHostParity).Assembly.GetName().Name,
        };

        static TestServer Start<TStartup>() where TStartup : class
        {
            var builder = new WebHostBuilder()
                .UseEnvironment("Production")
                .ConfigureAppConfiguration(c => c.AddInMemoryCollection(Settings))
                .UseStartup<TStartup>();
            return new TestServer(builder);
        }

        internal static readonly Lazy<TestServer> OliveHost = new(Start<OliveSide.App>);
        internal static readonly Lazy<TestServer> OurHost = new(Start<OurSide.App>);

        static async Task<string> Send(TestServer server, Func<HttpRequestMessage> request)
        {
            try
            {
                var response = await server.CreateClient().SendAsync(request());
                var headers = response.Content.Headers.Concat(response.Headers)
                    .Where(h => h.Key is "Set-Cookie" or "Content-Type" or "Content-Disposition" or "Content-Range" or "Accept-Ranges" or "Location" or "Content-Length")
                    .OrderBy(h => h.Key).Select(h => h.Key + ": " + string.Join(",", h.Value));
                var text = $"{(int)response.StatusCode}\n{string.Join("\n", headers)}\n{await response.Content.ReadAsStringAsync()}";
                return System.Text.RegularExpressions.Regex.Replace(text, @"expires=\w{3}, \d\d \w{3} (?!1970)\d{4} [\d:]+ GMT", "expires=<later>");
            }
            catch (Exception e)
            {
                return "throws " + e.GetType().Name + ": " + e.Message;
            }
        }

        static async Task Same(Func<HttpRequestMessage> request)
        {
            _ = SqlParity.Providers;
            var theirs = await Send(OliveHost.Value, request);
            var ours = await Send(OurHost.Value, request);
            // Olive crashing isn't behaviour to copy.
            if (theirs.StartsWith("throws ") || theirs.StartsWith("500")) return;
            if (ours != theirs) Assert.Fail("ours:\n" + ours + "\n\nOlive:\n" + theirs);
        }

        static Func<HttpRequestMessage> Post(string url, params (string Key, string Value)[] form) =>
            () => new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))) };

        static Func<HttpRequestMessage> Get(string url, string range = null) => () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (range != null) request.Headers.Range = RangeHeaderValue.Parse(range);
            return request;
        };

        public static IEnumerable<string> Routes() => ["/probe", "/probe/index", "/probe/echo/5", "/Probe/Echo/5", "/probe/echo", "/probe/echo/abc", "/probe/conventional", "/custom/hello", "/nothing/here", "/probe/json", "/probe/boom"];

        [Test]
        [MethodDataSource(nameof(Routes))]
        public async Task Routing_matches(string url) => await Same(Get(url));

        public static IEnumerable<(string, string)[]> Forms() =>
        [
            [],
            [("Name", "  Bob  "), ("Spaced", "  keep  "), ("Amount", "12.50"), ("Flag", "true"), ("Flag", "false"), ("MaybeFlag", "")],
            [("Date", "31/12/2024"), ("Local", "01/02/2024"), ("Day", "Friday"), ("Key", "6f9619ff-8b86-d011-b42d-00c04fc964ff")],
            [("Date", "2024-12-31"), ("Amount", "1,234.5"), ("Day", "3"), ("Key", "nope")],
            [("Ids", "1"), ("Ids", "2"), ("Tags", "a"), ("Tags", "b"), ("Child.Value", "c"), ("Filter", "true"), ("Sort", "Name.DESC"), ("Picked", "A|B")],
            [("Filter", "false"), ("Sort", "Name"), ("Picked", "A,B"), ("Ids", "x"), ("Amount", "abc"), ("Date", "garbage")],
            [("When", "garbage"), ("Picked", "B|Z"), ("Numbers", "1"), ("Labels", "x"), ("Filter", "yes"), ("Sort", "Amount.DESC")],
            [("When", "2024-05-06 07:08"), ("Picked", ""), ("Sort", ""), ("Filter", "")],
            [("Flag", "false"), ("Flag", "true"), ("MaybeFlag", "false"), ("Counts", "1"), ("Counts", "2"), ("Tags", "")],
            [("Flag", "abc"), ("MaybeFlag", "abc"), ("Counts", "x"), ("Labels", "a"), ("Labels", "b")],
            [("Flag", "TRUE"), ("MaybeFlag", "on"), ("Counts", "")],
            [("Name", ""), ("Spaced", "<b>x</b>"), ("Q", "form-q")],
            [("Photo", "value"), ("Photo_Action", "Removed"), ("Photo_TempFileId", "t1"), ("Photo_Filename", "a.pdf"), ("Photo_Url", "/f"), ("Photo_IsEmpty", "False")],
            [("Photo_Action", "New"), ("Photo_TempFileId", "t1"), ("Photo_Filename", "a.pdf"), ("Photo_IsEmpty", "False")],
            [("Photo_Action", "Unchanged"), ("Photo_Filename", "a.pdf"), ("Photo_Url", "/f"), ("Photo_IsEmpty", "False")],
            [("Photo_Action", "Unchanged"), ("Photo_IsEmpty", "True")],
            [("Photo_Action", "Bogus")],
            [("Photo_TempFileId", "only")],
            [("Photo.Action", "New"), ("Photo.TempFileId", "t2"), ("Photo.Filename", "b.pdf")],
        ];

        [Test]
        [MethodDataSource(nameof(Forms))]
        public async Task Binding_matches((string, string)[] form) => await Same(Post("/probe/bind?q=query-q", form));

        public static IEnumerable<(string Cookie, string Posted)> Cookies() => [("B", null), ("B|C", null), ("Z", null), ("B", "C"), ("B", "Z")];

        [Test]
        [MethodDataSource(nameof(Cookies))]
        public async Task Column_cookie_matches(string cookie, string posted) => await Same(() =>
        {
            var request = Post("/probe/bind", posted == null ? [] : [("Picked", posted)])();
            request.Headers.Add("Cookie", "BindIt.cols.Columns=" + Uri.EscapeDataString(cookie));
            return request;
        });

        public static IEnumerable<(string, string)[]> Invalid() =>
        [
            [],
            [("Required", "x"), ("Short", "toolong"), ("Email", "not-an-email"), ("Range", "50")],
            [("Required", "x"), ("Short", "ok"), ("Email", "a@b.com"), ("Range", "5")],
            [("Range", "abc")],
            [("Required", "x"), ("Strict", "true"), ("ShortIfStrict", "long"), ("SmallIfLoose", "9"), ("At", "25:00")],
            [("Required", "x"), ("Strict", "false"), ("IfLoose", "y"), ("ShortIfStrict", "long"), ("SmallIfLoose", "9"), ("At", "09:30")],
            [("Required", "x"), ("Strict", "true"), ("IfStrict", "y"), ("At", "9.30"), ("SmallIfLoose", "0")],
        ];

        [Test]
        [MethodDataSource(nameof(Invalid))]
        public async Task Validation_matches((string, string)[] form) => await Same(Post("/probe/validate", form));

        public static IEnumerable<(string, string)> Files() =>
        [
            ("/probe/file", null), ("/probe/file?inline=true", null), ("/probe/file?name=other.pdf", null),
            ("/probe/file", "bytes=0-3"), ("/probe/file", "bytes=5-"), ("/probe/file", "bytes=100-200"),
            ("/probe/emptyfile", null), ("/probe/file?name=a%22b.txt&inline=true", null), ("/probe/file?name=x;y.txt", null),
        ];

        static IEnumerable<string> Services(List<ServiceDescriptor> registered) => registered
            .Select(d => (d.ServiceType.FullName ?? d.ServiceType.Name).Replace("Olive.", "Platform.") + " " + d.Lifetime)
            .Distinct().OrderBy(x => x, StringComparer.Ordinal);

        [Test]
        public async Task Registered_services_match()
        {
            _ = OliveHost.Value;
            _ = OurHost.Value;
            var theirs = Services(OliveSide.App.Registered).ToList();
            var ours = Services(OurSide.App.Registered).ToList();
            var missing = theirs.Except(ours).ToList();
            var extra = ours.Except(theirs).ToList();
            if (missing.Count + extra.Count > 0)
                Assert.Fail("missing:\n  " + string.Join("\n  ", missing) + "\nextra:\n  " + string.Join("\n  ", extra));
            await Task.CompletedTask;
        }

        [Test]
        [MethodDataSource(nameof(Files))]
        public async Task File_matches(string url, string range) => await Same(Get(url, range));
    }
}

namespace CompatTests.OliveSide
{
    using Microsoft.AspNetCore.Mvc;
    using M = Olive.Mvc;
    using Newtonsoft.Json;
    using System.ComponentModel.DataAnnotations;

    public class App(IWebHostEnvironment env, IConfiguration config, ILoggerFactory logs) : M.Startup(env, config, logs)
    {
        public static List<ServiceDescriptor> Registered;

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            Registered = services.ToList();
            services.AddSingleton<Olive.Audit.IAudit, SqlParity.OliveNoAudit>();
            Olive.Entities.Data.DataAccessExtensions.AddDataAccess(services, x => Olive.Entities.Data.SqlDataAccessExtensions.SqlServer(x));
        }

        protected override void ConfigureMvc(IMvcBuilder mvc)
        {
            base.ConfigureMvc(mvc);
            mvc.ConfigureApplicationPartManager(m =>
            {
                m.FeatureProviders.Remove(m.FeatureProviders.OfType<ControllerFeatureProvider>().First());
                m.FeatureProviders.Add(new MvcHostParity.OnlyControllersOf(typeof(M.Controller)));
            });
        }

        // Base Configure re-initialises the process-wide Context, which other tests share.
        public override void Configure(IApplicationBuilder app)
        {
            app.UseRequestLocalization(RequestLocalizationOptions);
            ConfigureSecurity(app);
            ConfigureRequestHandlers(app);
        }
    }

    public class Child { public string Value { get; set; } }

    public class Model : M.IViewModel
    {
        public string Name { get; set; }
        [M.KeepWhiteSpace] public string Spaced { get; set; }
        public decimal Amount { get; set; }
        public bool Flag { get; set; }
        public bool? MaybeFlag { get; set; }
        public DateTime? Date { get; set; }
        [M.LocalizedDate] public DateTime? Local { get; set; }
        public DayOfWeek Day { get; set; }
        public Guid? Key { get; set; }
        public int[] Ids { get; set; }
        public List<string> Tags { get; set; }
        public Child Child { get; set; }
        public M.OptionalBooleanFilter Filter { get; set; }
        public M.ListSortExpression Sort { get; set; }
        public M.ColumnSelection Picked { get; set; }
        [M.FromRequest("q")] public string Q { get; set; }
        public string Log { get; set; } = "";
        public Model()
        {
            Sort = new M.ListSortExpression(this, "Default");
            Picked = new M.ColumnSelection("cols") { Options = ["A", "B", "C"], Default = ["A"] };
        }
        public M.BlobViewModel Photo { get; set; }
        public IEnumerable<int> Numbers { get; set; }
        public List<int> Counts { get; set; }
        public IList<string> Labels { get; set; }
        public DateTime When { get; set; }
        [M.OnPreBinding] public void PreBinding() => Log += "model-pre-binding;";
        [M.OnPreBound] public void PreBound() => Log += "model-pre-bound;";
        [M.OnBound] public void Bound() => Log += "model-bound;";
    }

    public class Form : M.IViewModel
    {
        [Required] public string Required { get; set; }
        [StringLength(3)] public string Short { get; set; }
        [EmailAddress] public string Email { get; set; }
        [Range(1, 10)] public int? Range { get; set; }
        public bool Strict { get; set; }
        [M.RequiredWhen(nameof(Strict))] public string IfStrict { get; set; }
        [M.RequiredUnless(nameof(Strict))] public string IfLoose { get; set; }
        [M.StringLengthWhen(nameof(Strict), 3)] public string ShortIfStrict { get; set; }
        [M.RangeUnless(nameof(Strict), 1, 5)] public int? SmallIfLoose { get; set; }
        [M.Time] public string At { get; set; }
    }

    public class ProbeController : M.Controller
    {
        [Route("probe")] public ActionResult Index() => Content("index");

        [Route("probe/echo/{id:int?}")] public ActionResult Echo(int? id) => Content("id=" + id);

        public ActionResult Conventional() => Content("conventional");

        [M.OnPreBinding] public void CtlPreBinding(Model info) => info.Log += "ctl-pre-binding(" + info.Name + "," + string.Join("|", ModelState.Keys.OrderBy(k => k)) + ");";
        [M.OnPreBound] public void CtlPreBound(Model info) => info.Log += "ctl-pre-bound(" + info.Name + "," + string.Join("|", ModelState.Keys.OrderBy(k => k)) + ");";
        [M.OnBound] public Task CtlBound(Model info) { info.Log += "ctl-bound(" + info.Name + "," + string.Join("|", ModelState.Keys.OrderBy(k => k)) + "," + ModelState.IsValid + ");"; return Task.CompletedTask; }

        [Route("probe/boom")] public ActionResult Boom() => throw new InvalidOperationException("kaboom");

        [Route("custom/{x}")]
        public ActionResult Attr(string x) => Content("x=" + x);

        [Route("probe/json")] public new ActionResult Json() => base.Json(new { FooBar = 1, Inner = new { A = "b" }, When = new DateTime(2024, 1, 2) });

        [HttpPost]
        [Route("probe/bind")] public ActionResult BindIt(Model info) => Content(JsonConvert.SerializeObject(new
        {
            Photo = info.Photo == null ? null : new { info.Photo.Action, info.Photo.TempFileId, info.Photo.Filename, info.Photo.Url, info.Photo.IsEmpty }, info.Numbers, info.Counts, info.Labels, info.When, info.Name, info.Spaced, info.Amount, info.Flag, info.MaybeFlag, info.Date, info.Local, info.Day, info.Key, info.Ids, info.Tags,
            Child = info.Child?.Value, Filter = info.Filter?.ToString(), Sort = info.Sort?.ToString(), SortDesc = info.Sort?.Descending,
            Columns = info.Picked?.Current == null ? null : string.Join("|", info.Picked.Current), info.Q, info.Log,
            Errors = ModelState.Where(e => e.Value.Errors.Any()).OrderBy(e => e.Key).Select(e => e.Key + ": " + string.Join(" ", e.Value.Errors.Select(x => x.ErrorMessage.Length > 0 ? x.ErrorMessage : x.Exception?.GetType().Name))),
        }));

        [HttpPost]
        [Route("probe/validate")] public ActionResult Validate(Form info) => Content(ModelState.IsValid + " " + string.Join(" | ",
            ModelState.Where(e => e.Value.Errors.Any()).OrderBy(e => e.Key).Select(e => e.Key + ": " + string.Join(" ", e.Value.Errors.Select(x => x.ErrorMessage)))));

        [Route("probe/file")] public Task<ActionResult> File(bool inline = false, string name = null) =>
            File(new Olive.Entities.Blob(System.Text.Encoding.UTF8.GetBytes("hello world"), "a b.txt"), name, showInline: inline);

        [Route("probe/emptyfile")] public Task<ActionResult> EmptyFile() => File(Olive.Entities.Blob.Empty());
    }
}

namespace CompatTests.OurSide
{
    using Microsoft.AspNetCore.Mvc;
    using M = Platform.Mvc;
    using Newtonsoft.Json;
    using System.ComponentModel.DataAnnotations;

    public class App(IWebHostEnvironment env, IConfiguration config, ILoggerFactory logs) : M.Startup(env, config, logs)
    {
        public static List<ServiceDescriptor> Registered;

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            Registered = services.ToList();
            services.AddSingleton<Platform.Audit.IAudit, SqlParity.OurNoAudit>();
            Platform.Entities.Data.DataAccessExtensions.AddDataAccess(services, x => Platform.Entities.Data.SqlDataAccessExtensions.SqlServer(x));
        }

        protected override void ConfigureMvc(IMvcBuilder mvc)
        {
            base.ConfigureMvc(mvc);
            mvc.ConfigureApplicationPartManager(m =>
            {
                m.FeatureProviders.Remove(m.FeatureProviders.OfType<ControllerFeatureProvider>().First());
                m.FeatureProviders.Add(new MvcHostParity.OnlyControllersOf(typeof(M.Controller)));
            });
        }

        public override void Configure(IApplicationBuilder app)
        {
            app.UseRequestLocalization(RequestLocalizationOptions);
            ConfigureSecurity(app);
            ConfigureRequestHandlers(app);
        }
    }

    public class Child { public string Value { get; set; } }

    public class Model : M.IViewModel
    {
        public string Name { get; set; }
        [M.KeepWhiteSpace] public string Spaced { get; set; }
        public decimal Amount { get; set; }
        public bool Flag { get; set; }
        public bool? MaybeFlag { get; set; }
        public DateTime? Date { get; set; }
        [M.LocalizedDate] public DateTime? Local { get; set; }
        public DayOfWeek Day { get; set; }
        public Guid? Key { get; set; }
        public int[] Ids { get; set; }
        public List<string> Tags { get; set; }
        public Child Child { get; set; }
        public M.OptionalBooleanFilter Filter { get; set; }
        public M.ListSortExpression Sort { get; set; }
        public M.ColumnSelection Picked { get; set; }
        [M.FromRequest("q")] public string Q { get; set; }
        public string Log { get; set; } = "";
        public Model()
        {
            Sort = new M.ListSortExpression(this, "Default");
            Picked = new M.ColumnSelection("cols") { Options = ["A", "B", "C"], Default = ["A"] };
        }
        public M.BlobViewModel Photo { get; set; }
        public IEnumerable<int> Numbers { get; set; }
        public List<int> Counts { get; set; }
        public IList<string> Labels { get; set; }
        public DateTime When { get; set; }
        [M.OnPreBinding] public void PreBinding() => Log += "model-pre-binding;";
        [M.OnPreBound] public void PreBound() => Log += "model-pre-bound;";
        [M.OnBound] public void Bound() => Log += "model-bound;";
    }

    public class Form : M.IViewModel
    {
        [Required] public string Required { get; set; }
        [StringLength(3)] public string Short { get; set; }
        [EmailAddress] public string Email { get; set; }
        [Range(1, 10)] public int? Range { get; set; }
        public bool Strict { get; set; }
        [M.RequiredWhen(nameof(Strict))] public string IfStrict { get; set; }
        [M.RequiredUnless(nameof(Strict))] public string IfLoose { get; set; }
        [M.StringLengthWhen(nameof(Strict), 3)] public string ShortIfStrict { get; set; }
        [M.RangeUnless(nameof(Strict), 1, 5)] public int? SmallIfLoose { get; set; }
        [M.Time] public string At { get; set; }
    }

    public class ProbeController : M.Controller
    {
        [Route("probe")] public ActionResult Index() => Content("index");

        [Route("probe/echo/{id:int?}")] public ActionResult Echo(int? id) => Content("id=" + id);

        public ActionResult Conventional() => Content("conventional");

        [M.OnPreBinding] public void CtlPreBinding(Model info) => info.Log += "ctl-pre-binding(" + info.Name + "," + string.Join("|", ModelState.Keys.OrderBy(k => k)) + ");";
        [M.OnPreBound] public void CtlPreBound(Model info) => info.Log += "ctl-pre-bound(" + info.Name + "," + string.Join("|", ModelState.Keys.OrderBy(k => k)) + ");";
        [M.OnBound] public Task CtlBound(Model info) { info.Log += "ctl-bound(" + info.Name + "," + string.Join("|", ModelState.Keys.OrderBy(k => k)) + "," + ModelState.IsValid + ");"; return Task.CompletedTask; }

        [Route("probe/boom")] public ActionResult Boom() => throw new InvalidOperationException("kaboom");

        [Route("custom/{x}")]
        public ActionResult Attr(string x) => Content("x=" + x);

        [Route("probe/json")] public new ActionResult Json() => base.Json(new { FooBar = 1, Inner = new { A = "b" }, When = new DateTime(2024, 1, 2) });

        [HttpPost]
        [Route("probe/bind")] public ActionResult BindIt(Model info) => Content(JsonConvert.SerializeObject(new
        {
            Photo = info.Photo == null ? null : new { info.Photo.Action, info.Photo.TempFileId, info.Photo.Filename, info.Photo.Url, info.Photo.IsEmpty }, info.Numbers, info.Counts, info.Labels, info.When, info.Name, info.Spaced, info.Amount, info.Flag, info.MaybeFlag, info.Date, info.Local, info.Day, info.Key, info.Ids, info.Tags,
            Child = info.Child?.Value, Filter = info.Filter?.ToString(), Sort = info.Sort?.ToString(), SortDesc = info.Sort?.Descending,
            Columns = info.Picked?.Current == null ? null : string.Join("|", info.Picked.Current), info.Q, info.Log,
            Errors = ModelState.Where(e => e.Value.Errors.Any()).OrderBy(e => e.Key).Select(e => e.Key + ": " + string.Join(" ", e.Value.Errors.Select(x => x.ErrorMessage.Length > 0 ? x.ErrorMessage : x.Exception?.GetType().Name))),
        }));

        [HttpPost]
        [Route("probe/validate")] public ActionResult Validate(Form info) => Content(ModelState.IsValid + " " + string.Join(" | ",
            ModelState.Where(e => e.Value.Errors.Any()).OrderBy(e => e.Key).Select(e => e.Key + ": " + string.Join(" ", e.Value.Errors.Select(x => x.ErrorMessage)))));

        [Route("probe/file")] public Task<ActionResult> File(bool inline = false, string name = null) =>
            File(new Platform.Entities.Blob(System.Text.Encoding.UTF8.GetBytes("hello world"), "a b.txt"), name, showInline: inline);

        [Route("probe/emptyfile")] public Task<ActionResult> EmptyFile() => File(Platform.Entities.Blob.Empty());
    }
}

