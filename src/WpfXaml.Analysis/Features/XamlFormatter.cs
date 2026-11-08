using WpfXaml.Language;
using WpfXaml.Language.Syntax;
using WpfXaml.Language.Text;

namespace WpfXaml.Analysis.Features;

/// <summary>
/// Re-indents XAML and collapses the spacing between attributes on a line. It never adds or removes lines,
/// reorders attributes, or touches text content, comments, CDATA, attribute values, or anything under
/// <c>xml:space="preserve"</c>. Documents with syntax errors are left alone.
/// </summary>
public static class XamlFormatter
{
    public static IReadOnlyList<(TextSpan Span, string NewText)> Format(XamlDocument document, int indentSize, bool insertSpaces)
    {
        if (document.Diagnostics.Any(d => d.Severity == XamlDiagnosticSeverity.Error))
        {
            return [];
        }

        var formatter = new Formatter(document, Math.Max(indentSize, 1), insertSpaces);
        return formatter.Run();
    }

    private sealed class Formatter(XamlDocument document, int indentSize, bool insertSpaces)
    {
        private readonly string _text = document.Text;

        /// <summary>Line-start indentation wanted when a line's first character is at this offset.</summary>
        private readonly Dictionary<int, string> _anchors = [];

        /// <summary>Ranges whose lines are never re-indented (start exclusive, end exclusive).</summary>
        private readonly List<TextSpan> _protected = [];

        private readonly List<(TextSpan Span, string NewText)> _edits = [];

        public IReadOnlyList<(TextSpan Span, string NewText)> Run()
        {
            foreach (var node in document.Nodes)
            {
                Visit(node, depth: 0, preserved: false);
            }

            ReindentLines();
            return _edits.OrderBy(e => e.Span.Start).ToList();
        }

        private void Visit(XamlNode node, int depth, bool preserved)
        {
            switch (node)
            {
                case XamlComment or XamlCData or XamlProcessingInstruction:
                    if (!preserved && node is XamlComment)
                    {
                        _anchors[node.Span.Start] = Indent(depth);
                    }

                    _protected.Add(node.Span);
                    break;

                case XamlElement element:
                    VisitElement(element, depth, preserved);
                    break;
            }
        }

        private void VisitElement(XamlElement element, int depth, bool preserved)
        {
            if (!preserved)
            {
                var indent = Indent(depth);
                _anchors[element.StartTagSpan.Start] = indent;
                if (element.EndTagSpan is { } endTag)
                {
                    _anchors[endTag.Start] = indent;
                }

                AnchorStartTag(element, indent);
                CollapseAttributeSpacing(element);
            }

            foreach (var attribute in element.Attributes)
            {
                if (attribute.ValueSpan is { } value)
                {
                    _protected.Add(TextSpan.FromBounds(value.Start - 1, value.End));
                }
            }

            if (IsPreserveSpace(element))
            {
                // Everything after the start tag, including the end tag's indentation, is content.
                _protected.Add(TextSpan.FromBounds(element.StartTagSpan.End - 1, element.Span.End));
                preserved = true;
            }

            foreach (var child in element.Children)
            {
                Visit(child, depth + 1, preserved);
            }
        }

        /// <summary>Attributes and the closing <c>&gt;</c> or <c>/&gt;</c> that start a line of a multi-line start tag.</summary>
        private void AnchorStartTag(XamlElement element, string indent)
        {
            var attributes = element.Attributes;
            if (attributes.Count > 0)
            {
                var first = attributes[0];
                var firstOnTagLine = !HasNewline(element.StartTagSpan.Start, first.Span.Start);
                var continuation = firstOnTagLine
                    ? indent + new string(' ', element.Name.FullName.Length + 2)
                    : indent + Unit;
                foreach (var attribute in attributes)
                {
                    _anchors[attribute.Span.Start] = continuation;
                }
            }

            var end = element.StartTagSpan.End;
            if (element.IsStartTagClosed)
            {
                _anchors[element.IsSelfClosing ? end - 2 : end - 1] = indent;
            }
        }

        private void CollapseAttributeSpacing(XamlElement element)
        {
            var previousEnd = element.Name.Span.End;
            foreach (var attribute in element.Attributes)
            {
                var gap = TextSpan.FromBounds(previousEnd, attribute.Span.Start);
                var text = _text[gap.Start..gap.End];
                if (text != " " && text.Length > 0 && text.All(c => c is ' ' or '\t'))
                {
                    _edits.Add((gap, " "));
                }

                previousEnd = attribute.Span.End;
            }
        }

        private void ReindentLines()
        {
            var lineStart = 0;
            while (lineStart <= _text.Length)
            {
                var first = lineStart;
                while (first < _text.Length && _text[first] is ' ' or '\t')
                {
                    first++;
                }

                if (first < _text.Length && _text[first] is not ('\r' or '\n')
                    && !_protected.Any(p => p.Start < first && first < p.End)
                    && _anchors.TryGetValue(first, out var indent)
                    && _text.AsSpan(lineStart, first - lineStart).SequenceCompareTo(indent) != 0)
                {
                    _edits.Add((TextSpan.FromBounds(lineStart, first), indent));
                }

                var newline = _text.IndexOf('\n', first);
                if (newline < 0)
                {
                    break;
                }

                lineStart = newline + 1;
            }
        }

        private bool HasNewline(int start, int end) => _text.AsSpan(start, end - start).Contains('\n');

        private string Unit => insertSpaces ? new string(' ', indentSize) : "\t";

        private string Indent(int depth) => insertSpaces ? new string(' ', depth * indentSize) : new string('\t', depth);

        private static bool IsPreserveSpace(XamlElement element) =>
            element.Attributes.Any(a => a.Name.Prefix == "xml" && a.Name.LocalName == "space" && a.Value == "preserve");
    }
}
