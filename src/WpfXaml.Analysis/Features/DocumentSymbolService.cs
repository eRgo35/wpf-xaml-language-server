using WpfXaml.Language.Syntax;
using WpfXaml.Language.Text;

namespace WpfXaml.Analysis.Features;

/// <summary>An element in the outline. <see cref="Name"/> is the element name plus its x:Name, Name or x:Key.</summary>
public sealed record XamlSymbol(string Name, bool IsPropertyElement, TextSpan Span, TextSpan NameSpan, IReadOnlyList<XamlSymbol> Children);

/// <summary>The element tree as an outline. Syntax only: works without a type system.</summary>
public static class DocumentSymbolService
{
    public static IReadOnlyList<XamlSymbol> GetSymbols(XamlDocument document) =>
        document.Root is { } root ? ToSymbols([root]) : [];

    private static IReadOnlyList<XamlSymbol> ToSymbols(IEnumerable<XamlElement> elements) =>
        elements
            .Where(e => !e.Name.IsEmpty)
            .Select(e => new XamlSymbol(Label(e), e.IsPropertyElement, e.Span, e.Name.Span, ToSymbols(e.ChildElements)))
            .ToList();

    private static string Label(XamlElement element)
    {
        // x:Name wins over Name (they're the same thing), and both over x:Key.
        var id = FindValue(element, "Name", prefixed: true)
            ?? FindValue(element, "Name", prefixed: false)
            ?? FindValue(element, "Key", prefixed: true);
        return string.IsNullOrEmpty(id) ? element.Name.FullName : $"{element.Name.FullName} {id}";
    }

    private static string? FindValue(XamlElement element, string localName, bool prefixed) =>
        element.Attributes.FirstOrDefault(a =>
            a.Name.LocalName == localName
            && (prefixed ? a.Name.Prefix is { } p && p != "xmlns" : a.Name.Prefix is null))?.Value;
}
