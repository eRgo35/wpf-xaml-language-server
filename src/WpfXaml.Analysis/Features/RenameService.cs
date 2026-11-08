using Microsoft.CodeAnalysis.CSharp;
using WpfXaml.Analysis.Semantics;
using WpfXaml.Language.Text;

namespace WpfXaml.Analysis.Features;

/// <param name="FilePath">Target file, or null for the current XAML document.</param>
public sealed record XamlTextEdit(string? FilePath, LinePosition Start, LinePosition End, string NewText);

/// <summary>The edits of a rename, or why it can't be done (then <see cref="Edits"/> is empty).</summary>
public sealed record RenameResult(IReadOnlyList<XamlTextEdit> Edits, string? Error);

/// <summary>Rename of <c>x:Key</c> and <c>x:Name</c>, with every reference <see cref="ReferenceService"/> finds.</summary>
public static class RenameService
{
    public static TextSpan? PrepareRename(XamlSemanticModel model, int offset) =>
        ReferenceService.FindNameTarget(model, offset)?.Span;

    public static RenameResult Rename(XamlSemanticModel model, int offset, string newName, IReadOnlyList<XamlSemanticModel> otherDocuments)
    {
        if (ReferenceService.FindNameTarget(model, offset) is not { } target)
        {
            return Fail("Only x:Key and x:Name values can be renamed.");
        }

        if (Validate(target.Kind, newName) is { } error)
        {
            return Fail(error);
        }

        var edits = ReferenceService.FindReferences(model, offset, otherDocuments, includeDeclaration: true)
            .Select(l => new XamlTextEdit(l.FilePath, l.Start, l.End, newName))
            .ToList();
        return new RenameResult(edits, null);
    }

    private static string? Validate(XamlNameKind kind, string newName)
    {
        if (kind == XamlNameKind.ElementName)
        {
            // x:Name becomes a field in the generated code-behind.
            return SyntaxFacts.IsValidIdentifier(newName) && SyntaxFacts.GetKeywordKind(newName) == SyntaxKind.None
                ? null
                : $"'{newName}' is not a valid name: x:Name must be a C# identifier.";
        }

        return newName.Length > 0 && !newName.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '{' or '}' or ',')
            ? null
            : $"'{newName}' is not a valid resource key: it must be non-empty, without spaces, quotes, braces or commas.";
    }

    private static RenameResult Fail(string error) => new([], error);
}
