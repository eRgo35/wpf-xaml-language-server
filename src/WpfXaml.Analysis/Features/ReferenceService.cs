using Microsoft.CodeAnalysis;
using WpfXaml.Analysis.Semantics;
using WpfXaml.Language.Syntax;
using WpfXaml.Language.Text;

namespace WpfXaml.Analysis.Features;

public enum XamlNameKind
{
    /// <summary>An <c>x:Key</c> and its <c>StaticResource</c>/<c>DynamicResource</c> uses.</summary>
    ResourceKey,

    /// <summary>An <c>x:Name</c>/<c>Name</c> and its <c>ElementName</c>, <c>TargetName</c>, <c>SourceName</c> and <c>x:Reference</c> uses.</summary>
    ElementName,
}

/// <summary>A renameable name under the caret. <see cref="Element"/> is the element the name is written on.</summary>
public sealed record XamlNameTarget(XamlNameKind Kind, string Name, TextSpan Span, XamlElement Element);

/// <summary>Find references for resource keys, element names, and the types and members XAML uses.</summary>
public static class ReferenceService
{
    private static readonly HashSet<string> TemplateElements = ["ControlTemplate", "DataTemplate", "ItemsPanelTemplate", "HierarchicalDataTemplate"];
    private static readonly HashSet<string> NameReferenceProperties = ["ElementName", "TargetName", "SourceName"];

    public static IReadOnlyList<XamlLocation> FindReferences(XamlSemanticModel model, int offset, IReadOnlyList<XamlSemanticModel> otherDocuments, bool includeDeclaration)
    {
        if (FindNameTarget(model, offset) is { } name)
        {
            if (name.Kind == XamlNameKind.ResourceKey)
            {
                return FindResourceOccurrences(model, name, otherDocuments, includeDeclaration);
            }

            var locations = FindNameOccurrences(model, name, includeDeclaration).Select(o => Location(model, o.Span, current: true));

            // Only the document's own namescope gets fields in the code-behind; template names don't.
            return NameScope(name.Element) is null
                ? locations.Concat(CodeBehindNames.FindUses(model, name.Name)).ToList()
                : locations.ToList();
        }

        return XamlTargetResolver.Resolve(model, offset)?.Symbol is { } symbol
            ? FindSymbolOccurrences(model, symbol, otherDocuments)
            : [];
    }

    public static XamlNameTarget? FindNameTarget(XamlSemanticModel model, int offset)
    {
        if (NameOccurrences(model).FirstOrDefault(o => o.Span.ContainsOrTouches(offset)) is { } occurrence)
        {
            return new XamlNameTarget(XamlNameKind.ElementName, occurrence.Name, occurrence.Span, occurrence.Element);
        }

        return XamlTargetResolver.Resolve(model, offset) is { Kind: XamlTargetKind.ResourceKey or XamlTargetKind.ResourceReference, Name: { } key, Element: { } element } target
            ? new XamlNameTarget(XamlNameKind.ResourceKey, key, target.Span, element)
            : null;
    }

    // ------------------------------------------------------------------ element names

    internal sealed record NameOccurrence(string Name, TextSpan Span, XamlElement Element, bool IsDeclaration);

    /// <summary>Declarations and uses of <paramref name="target"/> in its namescope, in this document.</summary>
    internal static IEnumerable<NameOccurrence> FindNameOccurrences(XamlSemanticModel model, XamlNameTarget target, bool includeDeclaration)
    {
        var scope = NameScope(target.Element);
        return NameOccurrences(model)
            .Where(o => o.Name == target.Name && NameScope(o.Element) == scope && (includeDeclaration || !o.IsDeclaration));
    }

    /// <summary>The template that starts the element's namescope, or null for the document's own namescope.</summary>
    internal static XamlElement? NameScope(XamlElement element) =>
        element.Ancestors().FirstOrDefault(a => TemplateElements.Contains(a.Name.LocalName));

    private static IEnumerable<NameOccurrence> NameOccurrences(XamlSemanticModel model)
    {
        var text = model.Document.Text;
        foreach (var element in model.Document.AllElements())
        {
            foreach (var attribute in element.Attributes)
            {
                if (model.GetMarkupExtension(attribute) is { Root: { } root })
                {
                    foreach (var extension in root.DescendantsAndSelf())
                    {
                        var isReference = extension.TypeName.LocalName is "Reference" or "ReferenceExtension";
                        foreach (var argument in extension.Arguments)
                        {
                            if (argument.Value.Extension is null
                                && (argument.Name == "ElementName" || (isReference && (argument.IsPositional || argument.Name == "Name")))
                                && Trim(text, argument.Value.Span) is { IsEmpty: false } span)
                            {
                                yield return new NameOccurrence(text[span.Start..span.End], span, element, IsDeclaration: false);
                            }
                        }
                    }

                    continue;
                }

                if (attribute.ValueSpan is not { } valueSpan || Trim(text, valueSpan) is not { IsEmpty: false } value)
                {
                    continue;
                }

                var name = attribute.Name;
                if (IsNameDeclaration(model, element, name))
                {
                    yield return new NameOccurrence(text[value.Start..value.End], value, element, IsDeclaration: true);
                }
                else if (name.Prefix is null && NameReferenceProperties.Contains(name.MemberName))
                {
                    yield return new NameOccurrence(text[value.Start..value.End], value, element, IsDeclaration: false);
                }
            }
        }
    }

    private static bool IsNameDeclaration(XamlSemanticModel model, XamlElement element, XamlName name) =>
        name.LocalName == "Name" && (name.Prefix is null || model.IsXamlLanguageNamespace(element, name.Prefix));

    // ------------------------------------------------------------------ resource keys

    /// <summary>Definitions (by file and key span) and uses of a resource key across documents.</summary>
    private static IReadOnlyList<XamlLocation> FindResourceOccurrences(XamlSemanticModel model, XamlNameTarget target, IReadOnlyList<XamlSemanticModel> otherDocuments, bool includeDeclaration)
    {
        var definitions = ResourceTargets(model, target);
        var locations = new List<XamlLocation>();
        foreach (var document in Documents(model, otherDocuments))
        {
            var current = document == model;
            foreach (var (span, isDefinition) in ResourceOccurrences(document, target.Name, definitions))
            {
                if (includeDeclaration || !isDefinition)
                {
                    locations.Add(Location(document, span, current));
                }
            }
        }

        return locations;
    }

    /// <summary>The definitions the target refers to, as (file path, key span); empty when the key is defined nowhere visible.</summary>
    private static HashSet<(string Path, TextSpan Span)> ResourceTargets(XamlSemanticModel model, XamlNameTarget target)
    {
        var scope = model.GetResourceScope(target.Element);
        var isDefinition = model.Document.AllElements().Any(e => KeySpan(model, e) == target.Span);
        return isDefinition
            ? [(PathOf(model), target.Span)]
            : scope.Find(target.Name).Select(d => (d.FilePath ?? PathOf(model), d.KeySpan)).ToHashSet();
    }

    private static IEnumerable<(TextSpan Span, bool IsDefinition)> ResourceOccurrences(XamlSemanticModel document, string key, HashSet<(string Path, TextSpan Span)> definitions)
    {
        var path = PathOf(document);
        var text = document.Document.Text;
        foreach (var element in document.Document.AllElements())
        {
            if (KeySpan(document, element) is { } keySpan && definitions.Contains((path, keySpan)))
            {
                yield return (keySpan, true);
            }

            foreach (var attribute in element.Attributes)
            {
                if (document.GetMarkupExtension(attribute) is not { Root: { } root })
                {
                    continue;
                }

                foreach (var extension in root.DescendantsAndSelf())
                {
                    if (document.ResolveMarkupExtensionType(element, extension.TypeName, out _) is not { } type
                        || !XamlTargetResolver.IsResourceExtension(document, type)
                        || extension.Arguments.FirstOrDefault(a => a.IsPositional || a.Name == "ResourceKey") is not { Value.Extension: null } argument
                        || Trim(text, argument.Value.Span) is not { IsEmpty: false } span
                        || text[span.Start..span.End] != key)
                    {
                        continue;
                    }

                    // A use counts when it resolves to one of the definitions (or, for an undefined key, by name).
                    var resolved = document.GetResourceScope(element).Find(key);
                    if (definitions.Count == 0 ? resolved.Count == 0 : resolved.Any(d => definitions.Contains((d.FilePath ?? path, d.KeySpan))))
                    {
                        yield return (span, false);
                    }
                }
            }
        }
    }

    private static TextSpan? KeySpan(XamlSemanticModel model, XamlElement element)
    {
        var key = element.Attributes.FirstOrDefault(a => a.Name.LocalName == "Key" && a.Name.Prefix is { } p && model.IsXamlLanguageNamespace(element, p));
        return key?.ValueSpan is { } span ? Trim(model.Document.Text, span) : null;
    }

    // ------------------------------------------------------------------ types and members

    private static IReadOnlyList<XamlLocation> FindSymbolOccurrences(XamlSemanticModel model, ISymbol symbol, IReadOnlyList<XamlSemanticModel> otherDocuments)
    {
        // Documents of other projects have their own compilations; compare by documentation id.
        var id = symbol.GetDocumentationCommentId();
        var locations = new List<XamlLocation>();
        foreach (var document in Documents(model, otherDocuments))
        {
            var seen = new HashSet<TextSpan>();
            foreach (var offset in CandidateOffsets(document))
            {
                if (XamlTargetResolver.Resolve(document, offset) is { Symbol: { } found } target
                    && (SymbolEqualityComparer.Default.Equals(found, symbol) || (id is not null && found.GetDocumentationCommentId() == id))
                    && seen.Add(target.Span))
                {
                    locations.Add(Location(document, target.Span, document == model));
                }
            }
        }

        return locations;
    }

    /// <summary>Start offsets of every name and value the target resolver can resolve.</summary>
    private static IEnumerable<int> CandidateOffsets(XamlSemanticModel model)
    {
        foreach (var element in model.Document.AllElements())
        {
            foreach (var offset in NameOffsets(element.Name))
            {
                yield return offset;
            }

            foreach (var attribute in element.Attributes)
            {
                foreach (var offset in NameOffsets(attribute.Name))
                {
                    yield return offset;
                }

                if (model.GetMarkupExtension(attribute) is { Root: { } root })
                {
                    foreach (var extension in root.DescendantsAndSelf())
                    {
                        yield return extension.TypeName.Span.Start;
                        foreach (var argument in extension.Arguments)
                        {
                            if (argument.NameSpan is { } nameSpan)
                            {
                                yield return nameSpan.Start;
                            }

                            if (argument.Value.Extension is null && Trim(model.Document.Text, argument.Value.Span) is { IsEmpty: false } value)
                            {
                                yield return value.Start;
                            }
                        }
                    }
                }
                else if (attribute.ValueSpan is { } valueSpan && Trim(model.Document.Text, valueSpan) is { IsEmpty: false } value)
                {
                    yield return value.Start;
                }
            }
        }
    }

    private static IEnumerable<int> NameOffsets(XamlName name)
    {
        if (name.IsEmpty)
        {
            yield break;
        }

        yield return name.Span.Start;
        if (name.IsDotted)
        {
            yield return name.MemberNameSpan.Start;
        }
    }

    // ------------------------------------------------------------------ helpers

    private static IEnumerable<XamlSemanticModel> Documents(XamlSemanticModel model, IReadOnlyList<XamlSemanticModel> otherDocuments) =>
        otherDocuments.Where(d => d != model).Prepend(model);

    private static string PathOf(XamlSemanticModel model) => model.Resources?.FilePath ?? string.Empty;

    private static XamlLocation Location(XamlSemanticModel document, TextSpan span, bool current)
    {
        var lines = document.Document.Lines;
        return new XamlLocation(current ? null : document.Resources?.FilePath, lines.GetPosition(span.Start), lines.GetPosition(span.End));
    }

    /// <summary>The span without leading and trailing whitespace.</summary>
    internal static TextSpan Trim(string text, TextSpan span)
    {
        var start = span.Start;
        var end = span.End;
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return TextSpan.FromBounds(start, end);
    }
}
