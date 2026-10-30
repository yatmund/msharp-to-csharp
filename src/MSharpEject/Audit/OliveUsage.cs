using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MSharpEject.Audit;

public record SymbolUse(string Assembly, string Symbol, string Kind, int Count, int GeneratedCount, int Files);

public static class OliveUsage
{
    public static bool IsGeeksAssembly(IAssemblySymbol? assembly) =>
        assembly != null && RepoScan.IsGeeksPackage(assembly.Name);

    /// <param name="progress">Fraction of syntax trees done; called from worker threads.</param>
    public static List<SymbolUse> Collect(IEnumerable<Compilation> compilations, Func<string, bool> isGeneratedPath,
        Action<double>? progress = null, CancellationToken cancel = default)
    {
        var uses = new ConcurrentDictionary<(string Assembly, string Symbol, string Kind), Tally>();

        // Vendored Olive source isn't usage of Olive.
        var targets = compilations.Where(c => !IsGeeksAssembly(c.Assembly)).ToList();
        var total = Math.Max(1, targets.Sum(c => c.SyntaxTrees.Count()));
        var done = 0;

        // One parallel pass over every tree in every project: per-project loops leave cores idle on small projects.
        var trees = targets.SelectMany(c => c.SyntaxTrees.Select(t => (Compilation: c, Tree: t))).ToList();
        Parallel.ForEach(trees, new ParallelOptions { CancellationToken = cancel }, item =>
        {
            var model = item.Compilation.GetSemanticModel(item.Tree);
            var generated = isGeneratedPath(item.Tree.FilePath);

            foreach (var name in item.Tree.GetRoot(cancel).DescendantNodes().OfType<SimpleNameSyntax>())
            {
                // Generated code missing from disk breaks overload resolution; candidates still name the Olive member.
                var info = model.GetSymbolInfo(name, cancel);
                var symbol = Normalise(info.Symbol ?? info.CandidateSymbols.FirstOrDefault());
                if (symbol == null || !IsGeeksAssembly(symbol.ContainingAssembly)) continue;

                var tally = uses.GetOrAdd((symbol.ContainingAssembly.Name, Display(symbol), Kind(symbol)), _ => new Tally());
                tally.Add(item.Tree.FilePath, generated);
            }
            progress?.Invoke((double)Interlocked.Increment(ref done) / total);
        });

        return uses
            .Select(u => new SymbolUse(u.Key.Assembly, u.Key.Symbol, u.Key.Kind, u.Value.Count, u.Value.Generated, u.Value.FileCount))
            .OrderByDescending(u => u.Count).ThenBy(u => u.Symbol, StringComparer.Ordinal)
            .ToList();
    }

    static ISymbol? Normalise(ISymbol? symbol) => symbol switch
    {
        null or INamespaceSymbol => null,
        IMethodSymbol { MethodKind: MethodKind.Constructor } c => c.ContainingType.OriginalDefinition,
        IMethodSymbol m => (m.ReducedFrom ?? m).OriginalDefinition,
        _ => symbol.OriginalDefinition,
    };

    static string Display(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol t => t.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
        _ => symbol.ContainingType is { } owner ? owner.Name + "." + symbol.Name : symbol.Name,
    };

    static string Kind(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { IsExtensionMethod: true } => "extension",
        INamedTypeSymbol t when t.IsSubclassOf("System.Attribute") => "attribute",
        INamedTypeSymbol t => t.TypeKind.ToString().ToLowerInvariant(),
        _ => symbol.Kind.ToString().ToLowerInvariant(),
    };

    static bool IsSubclassOf(this INamedTypeSymbol type, string baseName)
    {
        for (var b = type.BaseType; b != null; b = b.BaseType)
            if (b.ToDisplayString() == baseName) return true;
        return false;
    }

    sealed class Tally
    {
        readonly HashSet<string> files = [];
        public int Count, Generated;
        public int FileCount { get { lock (files) return files.Count; } }

        public void Add(string file, bool generated)
        {
            Interlocked.Increment(ref Count);
            if (generated) Interlocked.Increment(ref Generated);
            lock (files) files.Add(file);
        }
    }
}
