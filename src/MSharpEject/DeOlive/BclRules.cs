using Microsoft.CodeAnalysis;

namespace MSharpEject.DeOlive;

/// <param name="Template">{0} is the receiver, {1}.. the arguments.</param>
/// <param name="Negated">Used for !call(), so the output doesn't read !(!x).</param>
public record BclRule(string Method, string Template, string? Negated, bool NeedsLinq, Func<IMethodSymbol, bool> Matches);

/// <summary>
/// Olive extensions with an exact BCL equivalent, checked against Olive's source
/// (Olive/-Extensions/String.cs, Linq.cs). Null-safety must match: HasAny/None treat null as empty.
/// </summary>
public static class BclRules
{
    public static readonly BclRule[] All =
    [
        new("HasValue", "!string.IsNullOrEmpty({0})", "string.IsNullOrEmpty({0})", false, m => Receiver(m, SpecialType.System_String) && m.Parameters.Length == 1),
        new("IsEmpty", "string.IsNullOrEmpty({0})", "!string.IsNullOrEmpty({0})", false, m => Receiver(m, SpecialType.System_String) && m.Parameters.Length == 1),
        new("OrEmpty", "{0} ?? string.Empty", null, false, m => Receiver(m, SpecialType.System_String) && m.Parameters.Length == 1),
        new("HasAny", "{0}?.Any() == true", "{0}?.Any() != true", true, m => GenericEnumerableReceiver(m) && m.Parameters.Length == 1),
        new("None", "{0}?.Any() != true", "{0}?.Any() == true", true, m => GenericEnumerableReceiver(m) && m.Parameters.Length == 1),
        new("None", "!{0}.Any({1})", "{0}.Any({1})", true, m => GenericEnumerableReceiver(m) && m.Parameters.Length == 2 && IsFunc(m.Parameters[1].Type, 2)),
        new("Lacks", "!{0}.Contains({1})", "{0}.Contains({1})", true, m => GenericEnumerableReceiver(m) && m.Parameters.Length == 2 && m.Parameters[1].Type is ITypeParameterSymbol),
    ];

    public static BclRule? Match(IMethodSymbol? called)
    {
        if (called?.ReducedFrom is not { } definition) return null;
        definition = definition.OriginalDefinition;
        if (definition.ContainingType?.Name != "OliveExtensions" || definition.ContainingNamespace?.ToDisplayString() != "Olive") return null;
        return All.FirstOrDefault(r => r.Method == definition.Name && r.Matches(definition));
    }

    static bool Receiver(IMethodSymbol m, SpecialType type) => m.Parameters.Length > 0 && m.Parameters[0].Type.SpecialType == type;

    static bool GenericEnumerableReceiver(IMethodSymbol m) =>
        m.Parameters.Length > 0 && m.Parameters[0].Type is INamedTypeSymbol { IsGenericType: true } t
        && t.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
        && t.TypeArguments[0] is ITypeParameterSymbol;

    static bool IsFunc(ITypeSymbol type, int arity) =>
        type is INamedTypeSymbol { Name: "Func" } f && f.ContainingNamespace.ToDisplayString() == "System" && f.TypeArguments.Length == arity;
}
