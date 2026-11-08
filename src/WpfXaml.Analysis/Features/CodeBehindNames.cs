using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WpfXaml.Analysis.Semantics;
using WpfXaml.Language.Text;

namespace WpfXaml.Analysis.Features;

/// <summary>
/// Uses of an <c>x:Name</c> in the <c>x:Class</c> code-behind. The field for the name is generated into
/// <c>obj/*.g.cs</c> by the WPF build, which this server's compilation usually lacks, so a use either binds to
/// nothing or to that generated field. Uses that bind to anything else (a local, a parameter, another member)
/// are a different symbol and are left alone.
/// </summary>
public static class CodeBehindNames
{
    public static IReadOnlyList<XamlLocation> FindUses(XamlSemanticModel model, string name)
    {
        if (model.ClassSymbol is not { } classSymbol)
        {
            return [];
        }

        var compilation = model.Types.Compilation;
        var locations = new List<XamlLocation>();
        foreach (var reference in classSymbol.DeclaringSyntaxReferences)
        {
            var tree = reference.SyntaxTree;
            if (IsGenerated(tree.FilePath) || !compilation.ContainsSyntaxTree(tree))
            {
                continue;
            }

            var semantic = compilation.GetSemanticModel(tree);
            foreach (var identifier in reference.GetSyntax().DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (identifier.Identifier.ValueText == name && IsSimpleOrThisAccess(identifier) && IsGeneratedFieldUse(semantic, identifier, classSymbol))
                {
                    var span = identifier.Identifier.GetLocation().GetLineSpan();
                    locations.Add(new XamlLocation(
                        tree.FilePath,
                        new LinePosition(span.StartLinePosition.Line, span.StartLinePosition.Character),
                        new LinePosition(span.EndLinePosition.Line, span.EndLinePosition.Character)));
                }
            }
        }

        return locations;
    }

    /// <summary><c>name</c> or <c>this.name</c>, not <c>other.name</c>.</summary>
    private static bool IsSimpleOrThisAccess(IdentifierNameSyntax identifier) =>
        identifier.Parent is not MemberAccessExpressionSyntax access
        || access.Name != identifier
        || access.Expression is ThisExpressionSyntax;

    private static bool IsGeneratedFieldUse(SemanticModel semantic, IdentifierNameSyntax identifier, INamedTypeSymbol classSymbol)
    {
        var info = semantic.GetSymbolInfo(identifier);
        return info.Symbol switch
        {
            null => info.CandidateSymbols.IsEmpty,
            IFieldSymbol field => SymbolEqualityComparer.Default.Equals(field.ContainingType, classSymbol)
                && field.Locations.All(l => !l.IsInSource || IsGenerated(l.SourceTree?.FilePath)),
            _ => false,
        };
    }

    private static bool IsGenerated(string? path) =>
        path is not null && (path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase));
}
