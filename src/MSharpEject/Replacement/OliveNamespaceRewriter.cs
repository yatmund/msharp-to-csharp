using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MSharpEject.Replacement;

/// <summary>Points code at the replacement library: the root <c>Olive</c> namespace and renamed Olive types.</summary>
public sealed partial class OliveNamespaceRewriter(SemanticModel model) : CSharpSyntaxRewriter
{
    public int Renamed { get; private set; }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        var name = node.Identifier.ValueText;
        var replacement =
            name == "Olive" && NamesOliveRoot(node) ? ReplacementLibrary.Root
            : ReplacementLibrary.RenamedTypes.TryGetValue(name, out var renamed) && IsOliveType(node) ? renamed
            : null;
        if (replacement == null) return base.VisitIdentifierName(node);
        Renamed++;
        return node.WithIdentifier(SyntaxFactory.Identifier(node.Identifier.LeadingTrivia, replacement, node.Identifier.TrailingTrivia));
    }

    bool NamesOliveRoot(IdentifierNameSyntax node)
    {
        if (model.GetSymbolInfo(node).Symbol is INamespaceSymbol { ContainingNamespace.IsGlobalNamespace: true }) return true;
        // Usings of Olive namespaces whose package isn't loaded don't bind; the leftmost name still says Olive.
        return node.Ancestors().OfType<UsingDirectiveSyntax>().FirstOrDefault() is { NamespaceOrType: var target } && Leftmost(target) == node;
    }

    bool IsOliveType(IdentifierNameSyntax node) =>
        model.GetSymbolInfo(node).Symbol is INamedTypeSymbol t && ReplacementLibrary.IsOliveAssembly(t.ContainingAssembly?.Name);

    static NameSyntax Leftmost(TypeSyntax name) => name switch
    {
        QualifiedNameSyntax q => Leftmost(q.Left),
        AliasQualifiedNameSyntax a => a.Name,
        _ => (NameSyntax)name,
    };

    /// <summary>Razor: <c>@using</c>, <c>@inherits</c>, <c>@model</c>, <c>@inject</c> and qualified <c>Olive.X</c> names. Tag helper assemblies collapse to the library's one.</summary>
    public static string? Razor(string text)
    {
        var replaced = TagHelperAssembly().Replace(text, m => m.Groups["head"].Value + ReplacementLibrary.Root);
        replaced = RazorUsing().Replace(replaced, m => m.Groups["head"].Value + ReplacementLibrary.Root);
        replaced = QualifiedName().Replace(replaced, ReplacementLibrary.Root + ".");
        foreach (var (from, to) in ReplacementLibrary.RenamedTypes) replaced = Regex.Replace(replaced, $@"(?<![\w.]){from}(?=\.)", to);
        return replaced == text ? null : replaced;
    }

    [GeneratedRegex(@"(?<head>@addTagHelper\s+[^,\r\n]+,\s*)Olive(\.\w+)*", RegexOptions.Multiline)]
    private static partial Regex TagHelperAssembly();

    [GeneratedRegex(@"(?<head>^\s*@using\s+(static\s+)?(\w+\s*=\s*)?)Olive\b(?!\w)", RegexOptions.Multiline)]
    private static partial Regex RazorUsing();

    [GeneratedRegex(@"(?<![\w.])Olive\.(?=[A-Z])")]
    private static partial Regex QualifiedName();
}
