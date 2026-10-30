using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace MSharpEject.DeOlive;

public sealed class OliveBclRewriter(SemanticModel model) : CSharpSyntaxRewriter
{
    public Dictionary<string, int> Applied { get; } = [];
    public int SkippedInQueries { get; private set; }
    public bool NeedsLinq { get; private set; }

    public override SyntaxNode? VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.LogicalNotExpression) && node.Operand is InvocationExpressionSyntax call
            && Eligible(call) is { Negated: not null } rule)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(call)!;
            return Replace(rule, rule.Negated, visited, node);
        }
        return base.VisitPrefixUnaryExpression(node);
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // Bind against the original tree before children are rewritten.
        var rule = Eligible(node);
        var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
        return rule == null ? visited : Replace(rule, rule.Template, visited, visited);
    }

    BclRule? Eligible(InvocationExpressionSyntax node)
    {
        if (BclRules.Match(model.GetSymbolInfo(node).Symbol as IMethodSymbol) is not { } rule) return null;
        if (node.Expression is not MemberAccessExpressionSyntax access) return null;
        // a?.B.HasValue(): the receiver is a binding inside a ?. chain, not an expression on its own.
        if (access.Expression.DescendantNodesAndSelf().Any(n => n is MemberBindingExpressionSyntax or ElementBindingExpressionSyntax)) return null;
        // Olive turns these into SQL inside Database.GetList(x => ...); the BCL call might not translate.
        if (InExpressionTree(node))
        {
            SkippedInQueries++;
            return null;
        }
        return rule;
    }

    ExpressionSyntax Replace(BclRule rule, string template, InvocationExpressionSyntax visited, SyntaxNode triviaSource)
    {
        var access = (MemberAccessExpressionSyntax)visited.Expression;
        // (a + b).HasValue(): the brackets were only needed for the member access; ours replace them.
        var receiver = access.Expression is ParenthesizedExpressionSyntax p ? p.Expression : access.Expression;
        ExpressionSyntax[] values = [receiver, .. visited.ArgumentList.Arguments.Select(a => a.Expression)];

        // Placeholders become the real nodes wrapped in annotated parentheses; the simplifier drops the unneeded ones.
        var shape = SyntaxFactory.ParseExpression(string.Format(template, values.Select((_, i) => (object)$"__p{i}").ToArray()));
        var filled = shape.ReplaceNodes(
            shape.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Where(n => n.Identifier.Text.StartsWith("__p")),
            (original, _) => Parenthesize(values[int.Parse(original.Identifier.Text[3..])].WithoutTrivia()).WithTriviaFrom(original));

        Applied[rule.Method] = Applied.GetValueOrDefault(rule.Method) + 1;
        NeedsLinq |= rule.NeedsLinq;
        return Parenthesize(filled).WithTriviaFrom(triviaSource);
    }

    static ParenthesizedExpressionSyntax Parenthesize(ExpressionSyntax expression) =>
        SyntaxFactory.ParenthesizedExpression(expression).WithAdditionalAnnotations(Simplifier.Annotation);

    bool InExpressionTree(SyntaxNode node) =>
        node.Ancestors().OfType<LambdaExpressionSyntax>().Any(lambda =>
            model.GetTypeInfo(lambda).ConvertedType is INamedTypeSymbol { Name: "Expression" } t
            && t.ContainingNamespace.ToDisplayString() == "System.Linq.Expressions");
}
