using System.Reflection;

namespace CompatTests;

/// <summary>Reports which public Olive members the replacement library provides. Informational: gaps don't fail the build.</summary>
public class ApiCoverage
{
    static readonly string[] OliveAssemblies = ["Olive", "Olive.Entities", "Olive.Entities.Data", "Olive.Entities.Data.SqlServer", "Olive.Audit", "Olive.CSV", "Olive.Export", "Olive.ApiClient", "Olive.Microservices", "Olive.Web", "Olive.Mvc.Security", "Olive.Mvc", "Olive.Email"];

    [Test]
    public async Task Report()
    {
        var ours = Keys(typeof(Platform.Extensions).Assembly, "Platform", olive: false);
        var lines = new List<string>();
        foreach (var name in OliveAssemblies)
        {
            var theirs = Keys(System.Reflection.Assembly.Load(name), "Olive", olive: true);
            var missing = theirs.Where(k => !ours.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            lines.Add($"{name}: {theirs.Count - missing.Count}/{theirs.Count} members");
            lines.AddRange(missing.Select(m => "  missing " + m));
        }
        var report = Path.Combine(AppContext.BaseDirectory, "api-coverage.txt");
        await File.WriteAllLinesAsync(report, lines);
        Console.WriteLine(string.Join("\n", lines.Where(l => !l.StartsWith("  "))) + "\nFull list: " + report);
    }

    static HashSet<string> Keys(Assembly assembly, string root, bool olive)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in assembly.GetExportedTypes().Where(t => t.Namespace == root || t.Namespace?.StartsWith(root + ".") == true || t.Namespace == "System"))
        {
            var typeName = Mapped(type.FullName!);
            keys.Add("T:" + typeName);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var m in type.GetMethods(flags).Where(m => Visible(m) && (!m.IsSpecialName || m.Name.StartsWith("op_"))))
                Add(keys, $"M:{typeName}.{m.Name}`{m.GetGenericArguments().Length}({Params(m)})", m, olive);
            foreach (var c in type.GetConstructors(flags).Where(Visible)) Add(keys, $"C:{typeName}({Params(c)})", c, olive);
            foreach (var p in type.GetProperties(flags).Where(p => Visible(p.GetMethod ?? p.SetMethod))) Add(keys, $"P:{typeName}.{p.Name}", p.GetMethod ?? p.SetMethod, olive);
            foreach (var f in type.GetFields(flags).Where(f => (f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly) && (!type.IsEnum || f.IsLiteral)))
                Add(keys, $"F:{typeName}.{f.Name}", f.IsPublic, overridable: false, olive);
            foreach (var e in type.GetEvents(flags).Where(e => Visible(e.AddMethod))) keys.Add($"E:{typeName}.{e.Name}");
        }
        return keys;
    }

    static bool Visible(MethodBase m) => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;

    // Overridable members must match Olive's access exactly or an app's override won't compile.
    // Otherwise our public stands in for Olive's protected, never the reverse.
    static void Add(HashSet<string> keys, string key, MethodBase m, bool olive) =>
        Add(keys, key, m.IsPublic, m.IsVirtual && !m.IsFinal && !m.DeclaringType!.IsInterface, olive);

    static void Add(HashSet<string> keys, string key, bool isPublic, bool overridable, bool olive)
    {
        keys.Add(key + (isPublic ? "" : " protected") + (overridable ? " virtual" : ""));
        if (!olive && isPublic && !overridable) keys.Add(key + " protected");
    }

    static string Params(MethodBase m) => string.Join(",", m.GetParameters().Select(p => TypeName(p.ParameterType)));

    static string TypeName(Type t) =>
        t.IsGenericParameter ? (t.DeclaringMethod != null ? "!!" : "!") + t.GenericParameterPosition
        : t.IsArray ? TypeName(t.GetElementType()!) + "[]"
        : t.IsByRef ? TypeName(t.GetElementType()!) + "&"
        : t.IsGenericType ? $"{Mapped(t.GetGenericTypeDefinition().FullName!)}[{string.Join(",", t.GetGenericArguments().Select(TypeName))}]"
        : Mapped(t.FullName ?? t.Name);

    static string Mapped(string name) => name.Replace("Platform.EntitiesExtensions", "Olive.OliveEntitiesExtensions").Replace("Platform.WebExtensions", "Olive.OliveWebExtensions").Replace("Platform.SecurityExtensions", "Olive.OliveSecurityExtensions").Replace("Platform.Mvc.MvcExtensions", "Olive.Mvc.OliveMvcExtensions").Replace("Platform.Extensions", "Olive.OliveExtensions").Replace("Platform", "Olive");
}
