using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompatTests
{
    /// <summary>The same entity shapes on both sides; the generated SQL and parameters must match Olive's.</summary>
    public class SqlParity
    {
        static readonly Lazy<(Olive.Entities.IDatabase Olive, Platform.Entities.IDatabase Ours)> Databases = new(Setup);

        internal static (IServiceProvider Olive, IServiceProvider Ours) Providers
        {
            get
            {
                _ = Databases.Value;
                return providers;
            }
        }

        static (IServiceProvider Olive, IServiceProvider Ours) providers;

        // Both Contexts are process-wide, so they run on the MVC parity hosts' services.
        static (Olive.Entities.IDatabase, Platform.Entities.IDatabase) Setup()
        {
            var olive = MvcHostParity.OliveHost.Value.Services;
            Olive.Context.Initialize(olive, () => olive.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()?.HttpContext?.RequestServices ?? olive);
            Olive.Context.Current.GetService<Olive.Entities.Data.IDatabaseProviderConfig>().Configure();

            var ours = MvcHostParity.OurHost.Value.Services;
            Platform.Context.Initialize(ours, () => ours.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()?.HttpContext?.RequestServices ?? ours);
            Platform.Context.Current.GetService<Platform.Entities.Data.IDatabaseProviderConfig>().Configure();
            providers = (olive, ours);

            return (Olive.Context.Current.GetService<Olive.Entities.IDatabase>(), Platform.Context.Current.GetService<Platform.Entities.IDatabase>());
        }

        static string Normal(string sql) => Regex.Replace(Regex.Replace(sql ?? "", @"\s+", " "), @"\( | \)", m => m.Value.Trim()).Trim();

        public static IEnumerable<string> EntityTypes() => ["Person", "Tag", "Order", "Employee", "Manager", "Note"];

        [Test]
        [MethodDataSource(nameof(EntityTypes))]
        public async Task Commands_match(string name)
        {
            var (oliveDb, ourDb) = Databases.Value;
            var theirs = (Olive.Entities.Data.DataProvider)oliveDb.GetProvider(typeof(OliveModel.Person).Assembly.GetType("CompatTests.OliveModel." + name));
            var ours = (Platform.Entities.Data.DataProvider)ourDb.GetProvider(typeof(OurModel.Person).Assembly.GetType("CompatTests.OurModel." + name));

            await Assert.That(Normal(ours.InsertCommand)).IsEqualTo(Normal(theirs.InsertCommand));
            await Assert.That(Normal(ours.UpdateCommand)).IsEqualTo(Normal(theirs.UpdateCommand));
            await Assert.That(Normal(ours.DeleteCommand)).IsEqualTo(Normal(theirs.DeleteCommand));
            await Assert.That(Normal(ours.GetFields())).IsEqualTo(Normal(theirs.GetFields()));
            await Assert.That(Normal(ours.GetTables())).IsEqualTo(Normal(theirs.GetTables()));
        }

        public static IEnumerable<string> Queries() => QueryPairs.Keys;

        static readonly Guid Id = new("11111111-1111-1111-1111-111111111111");
        static readonly string Bob = "Bob";

        static readonly Dictionary<string, (Func<Olive.Entities.IDatabase, Olive.Entities.IDatabaseQuery> Olive, Func<Platform.Entities.IDatabase, Platform.Entities.IDatabaseQuery> Ours)> QueryPairs = new()
        {
            ["all"] = (d => d.Of<OliveModel.Person>(), d => d.Of<OurModel.Person>()),
            ["eq"] = (d => d.Of<OliveModel.Person>().Where(x => x.Name == Bob), d => d.Of<OurModel.Person>().Where(x => x.Name == Bob)),
            ["not-null"] = (d => d.Of<OliveModel.Person>().Where(x => x.Name != null), d => d.Of<OurModel.Person>().Where(x => x.Name != null)),
            ["not-equal"] = (d => d.Of<OliveModel.Person>().Where(x => x.Name != Bob), d => d.Of<OurModel.Person>().Where(x => x.Name != Bob)),
            ["starts"] = (d => d.Of<OliveModel.Person>().Where(x => x.Name.StartsWith("Bo")), d => d.Of<OurModel.Person>().Where(x => x.Name.StartsWith("Bo"))),
            ["ends"] = (d => d.Of<OliveModel.Person>().Where(x => x.Name.EndsWith("ob")), d => d.Of<OurModel.Person>().Where(x => x.Name.EndsWith("ob"))),
            ["contains"] = (d => d.Of<OliveModel.Person>().Where(x => x.Name.Contains("o")), d => d.Of<OurModel.Person>().Where(x => x.Name.Contains("o"))),
            ["gt"] = (d => d.Of<OliveModel.Person>().Where(x => x.Age > 18), d => d.Of<OurModel.Person>().Where(x => x.Age > 18)),
            ["flipped"] = (d => d.Of<OliveModel.Person>().Where(x => 18 < x.Age), d => d.Of<OurModel.Person>().Where(x => 18 < x.Age)),
            ["lte"] = (d => d.Of<OliveModel.Person>().Where(x => x.Salary <= 100m), d => d.Of<OurModel.Person>().Where(x => x.Salary <= 100m)),
            ["bool"] = (d => d.Of<OliveModel.Person>().Where(x => x.IsActive), d => d.Of<OurModel.Person>().Where(x => x.IsActive)),
            ["not-bool"] = (d => d.Of<OliveModel.Person>().Where(x => !x.IsActive), d => d.Of<OurModel.Person>().Where(x => !x.IsActive)),
            ["and"] = (d => d.Of<OliveModel.Person>().Where(x => x.IsActive && x.Age >= 21), d => d.Of<OurModel.Person>().Where(x => x.IsActive && x.Age >= 21)),
            ["or"] = (d => d.Of<OliveModel.Person>().Where(x => x.IsActive || x.Age >= 21), d => d.Of<OurModel.Person>().Where(x => x.IsActive || x.Age >= 21)),
            ["has-value"] = (d => d.Of<OliveModel.Person>().Where(x => Olive.OliveExtensions.HasValue(x.Name)), d => d.Of<OurModel.Person>().Where(x => Platform.Extensions.HasValue(x.Name))),
            ["date"] = (d => d.Of<OliveModel.Person>().Where(x => x.Born < new DateTime(2000, 1, 1)), d => d.Of<OurModel.Person>().Where(x => x.Born < new DateTime(2000, 1, 1))),
            ["nullable-id"] = (d => d.Of<OliveModel.Person>().Where(x => x.ManagerId == null), d => d.Of<OurModel.Person>().Where(x => x.ManagerId == null)),
            ["order"] = (d => d.Of<OliveModel.Person>().OrderBy(x => x.Name).ThenByDescending(x => x.Age), d => d.Of<OurModel.Person>().OrderBy(x => x.Name).ThenByDescending(x => x.Age)),
            ["top"] = (d => d.Of<OliveModel.Person>().Top(5), d => d.Of<OurModel.Person>().Top(5)),
            ["page"] = (d => Olive.Entities.DatabaseQueryExtensions.Page(d.Of<OliveModel.Person>().OrderBy(x => x.Name), 10, 20),
                        d => Platform.Entities.DatabaseQueryExtensions.Page(d.Of<OurModel.Person>().OrderBy(x => x.Name), 10, 20)),
            ["soft-delete"] = (d => d.Of<OliveModel.Note>(), d => d.Of<OurModel.Note>()),
            ["derived"] = (d => d.Of<OliveModel.Manager>().Where(x => x.Level > 1 && x.Name == Bob), d => d.Of<OurModel.Manager>().Where(x => x.Level > 1 && x.Name == Bob)),
        };

        [Test]
        [MethodDataSource(nameof(Queries))]
        public async Task Query_matches(string name)
        {
            var (oliveDb, ourDb) = Databases.Value;
            var (oliveQuery, ourQuery) = QueryPairs[name];
            var theirs = oliveQuery(oliveDb);
            var ours = ourQuery(ourDb);
            var theirSql = ((Olive.Entities.Data.DataProvider)theirs.Provider).GenerateSelectCommand(theirs);
            var ourSql = ((Platform.Entities.Data.DataProvider)ours.Provider).GenerateSelectCommand(ours);

            if (Normal(ourSql) != Normal(theirSql)) Assert.Fail($"\nours:  {Normal(ourSql)}\nOlive: {Normal(theirSql)}");
            await Assert.That(Params(ours.Parameters)).IsEqualTo(Params(theirs.Parameters));
        }

        static string Params(Dictionary<string, object> parameters) => string.Join(", ", parameters.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

        internal class OliveNoAudit : Olive.Audit.IAudit
        {
            public Task Log(Olive.Audit.IAuditEvent auditEvent) => Task.CompletedTask;
            public Task Log(string e, string d, string g = null) => Task.CompletedTask;
            public Task Log(string e, string d, Olive.Entities.IEntity i, string g = null) => Task.CompletedTask;
            public Task Log(string e, string d, Olive.Entities.IEntity i, string u, string ip, string g = null) => Task.CompletedTask;
            public Task LogDelete(Olive.Entities.IEntity entity) => Task.CompletedTask;
            public Task LogInsert(Olive.Entities.IEntity entity) => Task.CompletedTask;
            public Task LogUpdate(Olive.Entities.IEntity entity) => Task.CompletedTask;
        }

        internal class OurNoAudit : Platform.Audit.IAudit
        {
            public Task Log(Platform.Audit.IAuditEvent auditEvent) => Task.CompletedTask;
            public Task Log(string e, string d, string g = null) => Task.CompletedTask;
            public Task Log(string e, string d, Platform.Entities.IEntity i, string g = null) => Task.CompletedTask;
            public Task Log(string e, string d, Platform.Entities.IEntity i, string u, string ip, string g = null) => Task.CompletedTask;
            public Task LogDelete(Platform.Entities.IEntity entity) => Task.CompletedTask;
            public Task LogInsert(Platform.Entities.IEntity entity) => Task.CompletedTask;
            public Task LogUpdate(Platform.Entities.IEntity entity) => Task.CompletedTask;
        }
    }
}

namespace CompatTests.OliveModel
{
    using Olive.Entities;

    [TableName("People")]
    public class Person : GuidEntity { public string Name { get; set; } public int? Age { get; set; } public bool IsActive { get; set; } public DateTime Born { get; set; } public Guid? ManagerId { get; set; } [Calculated] public Person Manager => null; public decimal Salary { get; set; } public override string ToString() => Name; }
    public class Tag : GuidEntity { public string Title { get; set; } }
    [IdByDatabase] public class Order : IntEntity { [AutoNumber] public int Number { get; set; } public string Code { get; set; } }
    public class Employee : GuidEntity { public string Name { get; set; } }
    public class Manager : Employee { public int Level { get; set; } }
    [SoftDelete] public class Note : GuidEntity { public string Text { get; set; } }
}

namespace CompatTests.OurModel
{
    using Platform.Entities;

    [TableName("People")]
    public class Person : GuidEntity { public string Name { get; set; } public int? Age { get; set; } public bool IsActive { get; set; } public DateTime Born { get; set; } public Guid? ManagerId { get; set; } [Calculated] public Person Manager => null; public decimal Salary { get; set; } public override string ToString() => Name; }
    public class Tag : GuidEntity { public string Title { get; set; } }
    [IdByDatabase] public class Order : IntEntity { [AutoNumber] public int Number { get; set; } public string Code { get; set; } }
    public class Employee : GuidEntity { public string Name { get; set; } }
    public class Manager : Employee { public int Level { get; set; } }
    [SoftDelete] public class Note : GuidEntity { public string Text { get; set; } }
}
