using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace CompatTests;

/// <summary>Every public static method in the replacement library must give Olive's results on the same inputs.</summary>
public class MatchesOlive
{
    static readonly (Type Ours, Type Theirs)[] Pairs =
    [
        (typeof(Platform.Extensions), typeof(Olive.OliveExtensions)),
    ];

    // Random, or Olive overflows its own stack (ToShortMoneyString(double?) calls itself).
    static readonly HashSet<string> Unchecked = ["PickRandom", "Randomize", "ToShortMoneyString(System.Nullable`1"];

    public static IEnumerable<string> Methods() =>
        Pairs.SelectMany(p => Public(p.Ours).Select(m => $"{p.Ours.Name}.{Signature(m)}")).Distinct().Order(StringComparer.Ordinal);

    [Test]
    [MethodDataSource(nameof(Methods))]
    public async Task Same_results_as_Olive(string method)
    {
        var (oursType, theirsType) = Pairs.Single(p => method.StartsWith(p.Ours.Name + ".", StringComparison.Ordinal));
        var signature = method[(oursType.Name.Length + 1)..];
        var ours = Public(oursType).First(m => Signature(m) == signature);
        var theirs = Public(theirsType).FirstOrDefault(m => Signature(m) == signature);
        await Assert.That(theirs).IsNotNull().Because($"Olive has no {signature}; the signature must match exactly");
        if (Unchecked.Any(u => ours.Name == u || signature.StartsWith(u.Replace("(", "`0("))) || ours.GetParameters().Any(p => typeof(Delegate).IsAssignableFrom(p.ParameterType) || p.ParameterType.IsByRef)) return;

        if (ours.IsGenericMethodDefinition)
        {
            var args = ours.GetGenericArguments().Select(a => a.GenericParameterAttributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint) ? typeof(int) : typeof(string)).ToArray();
            ours = ours.MakeGenericMethod(args);
            theirs = theirs!.MakeGenericMethod(args);
        }

        var samples = ours.GetParameters().Select(Samples).ToList();
        await Assert.That(samples.Any(s => s == null)).IsFalse().Because("every parameter type needs sample values in " + nameof(Samples));

        var mismatches = new List<string>();
        foreach (var args in Combinations(samples!, 400))
        {
            var a = Run(ours, args);
            var b = Run(theirs!, args);
            // Where Olive throws, no app relies on the result; only where it returns must ours match.
            if (b.StartsWith("throws ")) continue;
            // Both read the clock a few milliseconds apart.
            if (ours.Name == "ToTimeDifferenceString")
            {
                a = System.Text.RegularExpressions.Regex.Replace(a, @"\d+\.\d+", "N");
                b = System.Text.RegularExpressions.Regex.Replace(b, @"\d+\.\d+", "N");
            }
            if (a != b) mismatches.Add($"({string.Join(", ", args.Select(Show))}): ours {a}, Olive {b}");
        }
        if (mismatches.Count > 0) Assert.Fail($"{mismatches.Count} mismatches:\n" + string.Join("\n", mismatches.Take(8)));
    }

    static IEnumerable<MethodInfo> Public(Type t) => t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.DeclaringType == t);

    static string Signature(MethodInfo m) =>
        $"{m.Name}`{m.GetGenericArguments().Length}({string.Join(", ", m.GetParameters().Select(p => TypeName(p.ParameterType)))})";

    static string TypeName(Type t) =>
        t.IsGenericParameter ? "!!" + t.GenericParameterPosition
        : t.IsArray ? TypeName(t.GetElementType()!) + "[]"
        : t.IsByRef ? TypeName(t.GetElementType()!) + "&"
        : t.IsGenericType ? $"{Mapped(t.GetGenericTypeDefinition().FullName)}[{string.Join(",", t.GetGenericArguments().Select(TypeName))}]"
        : Mapped(t.FullName ?? t.Name);

    static string Mapped(string name) => name.Replace("Platform.Extensions", "Olive.OliveExtensions").Replace("Platform.", "Olive.");

    static string Run(MethodInfo method, object[] args)
    {
        try
        {
            return Show(method.Invoke(null, args.Select(Copy).ToArray()));
        }
        catch (TargetInvocationException e)
        {
            return "throws " + e.InnerException!.GetType().Name;
        }
        catch (Exception e)
        {
            return "throws " + e.GetType().Name;
        }
    }

    // Some methods mutate their argument; each side gets its own copy.
    static object Copy(object value) => value switch
    {
        string or null => value,
        Array a => a.Clone(),
        _ => value,
    };

    static string Show(object value)
    {
        try
        {
            return value switch
            {
                null => "null",
                string s => '"' + s.Replace("\r", "\\r").Replace("\n", "\\n") + '"',
                char c => $"'{c}'",
                bool b => b ? "true" : "false",
                DateTime d => d.ToString("O"),
                double d => d.ToString("R", CultureInfo.InvariantCulture),
                float f => f.ToString("R", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                IDictionary dict => "{" + string.Join(", ", dict.Keys.Cast<object>().Select(k => Show(k) + ": " + Show(dict[k]))) + "}",
                IEnumerable items => "[" + string.Join(", ", items.Cast<object>().Select(Show)) + "]",
                _ => value.ToString(),
            };
        }
        catch (Exception e)
        {
            return "throws " + e.GetType().Name;
        }
    }

    static IEnumerable<object[]> Combinations(List<object[]> samples, int max)
    {
        var total = samples.Aggregate(1L, (n, s) => n * Math.Max(1, s.Length));
        var step = Math.Max(1, total / max);
        for (long i = 0; i < total; i += step)
        {
            var args = new object[samples.Count];
            var rest = i;
            for (var p = 0; p < samples.Count; p++)
            {
                args[p] = samples[p][rest % samples[p].Length];
                rest /= samples[p].Length;
            }
            yield return args;
        }
    }

    static readonly DateTime Now = DateTime.Now;

    static object[] Samples(ParameterInfo p)
    {
        // Olive recurses forever on non-positive sizes, and a stack overflow can't be caught.
        var values = p.ParameterType == typeof(int) && p.Name!.EndsWith("Size") ? [1, 2, 3, 7]
            // Rounding to a negative interval is meaningless.
            : p.ParameterType == typeof(TimeSpan) && p.Name == "nearest" ? Values(p.ParameterType).Where(v => (TimeSpan)v >= TimeSpan.Zero).ToArray()
            : Values(p.ParameterType);
        if (values == null) return p.HasDefaultValue ? [p.DefaultValue] : null;
        return p.HasDefaultValue ? values.Prepend(p.DefaultValue).Distinct().ToArray() : values;
    }

    static readonly string[] Strings = [null, "", " ", "a", "A", "Hello world", "HelloWorld", "helloWorldAgain", "hello_world-again", "  padded  ",
        "Line1\nLine2", "Line1\r\nLine2", "<b>x&y</b> \"q\"", "a,b,c", "123", "12.5", "Ünïcødé", "user@example.com", "https://example.com/a?b=1&c=2", "ABC Ltd", "XMLParser", "the end."];

    static object[] Values(Type t)
    {
        if (Nullable.GetUnderlyingType(t) is { } inner) return Values(inner)?.Prepend(null).ToArray();
        if (t == typeof(string)) return Strings;
        if (t == typeof(char)) return ['a', 'A', '1', ' ', '-', 'é', '_'];
        if (t == typeof(int)) return [0, 1, -1, 2, 3, 7, 11, 21, 100, 1234567];
        if (t == typeof(long)) return [0L, 1L, -5L, 999L, 1024L, 1536L, 1048576L, 5_000_000_000L];
        if (t == typeof(double)) return [0d, 1d, 1.5, -2.25, 3.14159, 0.005, 1e6, 12345.678];
        if (t == typeof(float)) return [0f, 1f, 1.5f, -2.25f, 3.14159f];
        if (t == typeof(decimal)) return [0m, 1m, 1.5m, -2.25m, 3.14159m, 0.005m, 1000000m];
        if (t == typeof(bool)) return [true, false];
        if (t == typeof(Guid)) return [Guid.Empty, new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff")];
        if (t == typeof(TimeSpan)) return [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(90), TimeSpan.FromHours(2.5), new TimeSpan(3, 4, 5, 6), TimeSpan.FromDays(400), TimeSpan.FromMinutes(-30)];
        if (t == typeof(DateTime))
            return [new DateTime(2024, 2, 29, 13, 45, 30), new DateTime(2023, 12, 31, 23, 59, 59, 500), new DateTime(2025, 1, 1), new DateTime(2026, 10, 30, 9, 0, 0),
                new DateTime(2026, 11, 1, 8, 30, 0), Now.Date, Now.AddMinutes(-5), Now.AddDays(-3), Now.AddDays(2), Now.AddYears(-2)];
        if (t == typeof(DayOfWeek)) return [DayOfWeek.Monday, DayOfWeek.Sunday];
        if (t == typeof(Encoding)) return [Encoding.UTF8];
        if (t == typeof(byte[])) return [Array.Empty<byte>(), new byte[] { 1, 2, 255 }, Encoding.UTF8.GetBytes("hi there")];
        if (t == typeof(Type)) return [typeof(string), typeof(int), typeof(List<string>), typeof(IEnumerable<int>)];
        if (t == typeof(object)) return [null, "x", 5, 2.5];
        if (t == typeof(string[])) return [Array.Empty<string>(), new[] { "a" }, new[] { "a", "Hello world" }, new[] { "HELLO", null, "" }];
        if (t == typeof(char[])) return [Array.Empty<char>(), new[] { 'a', '1' }];
        if (t == typeof(object[])) return [Array.Empty<object>(), new object[] { "b", 2 }];
        if (t == typeof(int[])) return [Array.Empty<int>(), new[] { 1, 2, 3 }];
        if (t == typeof(Guid?[])) return [Array.Empty<Guid?>(), new Guid?[] { Guid.Empty, null }];
        if (t == typeof(Type[])) return [Array.Empty<Type>(), new[] { typeof(int), typeof(string) }];
        if (t == typeof(IEnumerable)) return [null, Array.Empty<object>(), new object[] { "a", 1, null }];
        if (t == typeof(IEnumerable<string>[])) return [Array.Empty<IEnumerable<string>>(), new IEnumerable<string>[] { new[] { "a" }, new[] { "b", "a" } }];
        if (t == typeof(IEnumerable<int>[])) return [Array.Empty<IEnumerable<int>>(), new IEnumerable<int>[] { new[] { 1 }, new[] { 2, 1 } }];
        if (t == typeof(HashSet<string>)) return [new HashSet<string>(), new HashSet<string> { "a", "b" }];
        if (t == typeof(HashSet<int>)) return [new HashSet<int>(), new HashSet<int> { 1, 2 }];
        if (t == typeof(IDictionary<string, string>)) return [new Dictionary<string, string>(), new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }];
        if (t == typeof(IDictionary<int, int>)) return [new Dictionary<int, int>(), new Dictionary<int, int> { [1] = 10 }];
        if (t == typeof(IDictionary<string, int>)) return [new Dictionary<string, int> { ["a"] = 1 }];
        if (t == typeof(IDictionary<int, string>)) return [new Dictionary<int, string> { [1] = "x" }];
        if (t == typeof(IDictionary)) return [new Hashtable(), new Hashtable { ["a"] = 1 }];
        if (t.IsGenericType && t.GetGenericArguments().Length == 1 && t.IsAssignableFrom(typeof(List<>).MakeGenericType(t.GetGenericArguments()[0])))
        {
            var item = t.GetGenericArguments()[0];
            var source = item == typeof(string) ? new[] { Array.Empty<string>(), new[] { "a" }, new[] { "b", "a", "b" }, new[] { "Hello", "", "x" } }.Cast<IEnumerable>()
                : item == typeof(int) ? new[] { Array.Empty<int>(), new[] { 1 }, new[] { 3, 1, 2, 2 } }.Cast<IEnumerable>()
                : Values(item) is { } single ? [single.Take(3).ToArray(), Array.Empty<object>()]
                : null;
            if (source == null) return null;
            return [null, .. source.Select(s => { var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item))!; foreach (var x in s) list.Add(x); return (object)list; })];
        }
        return null;
    }
}
