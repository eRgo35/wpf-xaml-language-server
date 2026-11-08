using WpfXaml.Analysis.Features;
using WpfXaml.Language.Syntax;

namespace WpfXaml.Analysis.Tests;

public class FormatterTests
{
    private static string Format(string text, int indentSize = 2, bool insertSpaces = true)
    {
        var edits = XamlFormatter.Format(XamlDocument.Parse(text), indentSize, insertSpaces);
        var result = Apply(text, edits);

        // Every formatted result must be stable.
        Assert.Empty(XamlFormatter.Format(XamlDocument.Parse(result), indentSize, insertSpaces));
        return result;
    }

    private static string Apply(string text, IReadOnlyList<(Language.Text.TextSpan Span, string NewText)> edits)
    {
        foreach (var (span, newText) in edits.OrderByDescending(e => e.Span.Start))
        {
            text = text[..span.Start] + newText + text[span.End..];
        }

        return text;
    }

    [Fact]
    public void Format_ReindentsNested()
    {
        Assert.Equal(
            "<Window>\n  <Grid>\n    <Button />\n  </Grid>\n</Window>",
            Format("<Window>\n      <Grid>\n<Button />\n </Grid>\n   </Window>"));
    }

    [Fact]
    public void Format_TabsWhenRequested()
    {
        Assert.Equal("<Grid>\n\t<Button />\n</Grid>", Format("<Grid>\n    <Button />\n</Grid>", indentSize: 4, insertSpaces: false));
    }

    [Fact]
    public void Format_AlignsContinuationAttributes()
    {
        Assert.Equal(
            "<Grid>\n  <Button Content=\"a\"\n          Width=\"5\" />\n</Grid>",
            Format("<Grid>\n<Button Content=\"a\"\n      Width=\"5\" />\n</Grid>"));
    }

    [Fact]
    public void Format_IndentsAttributesOnOwnLines()
    {
        Assert.Equal(
            "<Grid>\n  <Button\n    Content=\"a\"\n    Width=\"5\"\n  />\n</Grid>",
            Format("<Grid>\n<Button\nContent=\"a\"\n        Width=\"5\"\n      />\n</Grid>"));
    }

    [Fact]
    public void Format_CollapsesAttributeSpacing()
    {
        Assert.Equal("<Button Content=\"a\" Width=\"5\" />", Format("<Button   Content=\"a\"    Width=\"5\" />"));
    }

    [Fact]
    public void Format_KeepsTextCommentsAndValues()
    {
        const string text = "<Grid>\n  <TextBlock>\n      first line\n  second line\n  </TextBlock>\n  <!-- a\n        comment -->\n  <Button Content=\"multi\n     line\" />\n</Grid>";

        Assert.Equal(text, Format(text));
    }

    [Fact]
    public void Format_IndentsComments()
    {
        Assert.Equal("<Grid>\n  <!-- note -->\n</Grid>", Format("<Grid>\n<!-- note -->\n</Grid>"));
    }

    [Fact]
    public void Format_PreserveSpaceUntouched()
    {
        const string text = "<Grid>\n  <TextBlock xml:space=\"preserve\">\n<Run>  a  </Run>\n      </TextBlock>\n</Grid>";

        Assert.Equal(text, Format(text));
    }

    [Fact]
    public void Format_CrLfKept()
    {
        Assert.Equal("<Grid>\r\n  <Button />\r\n</Grid>\r\n", Format("<Grid>\r\n<Button />\r\n</Grid>\r\n"));
    }

    [Fact]
    public void Format_SyntaxErrorsNoEdits()
    {
        Assert.Empty(XamlFormatter.Format(XamlDocument.Parse("<Grid>\n<Button Con\n</Grid>"), 2, true));
    }

    [Fact]
    public void Format_AlreadyFormattedNoEdits()
    {
        Assert.Empty(XamlFormatter.Format(XamlDocument.Parse("<Grid>\n  <Button Content=\"a\" />\n</Grid>"), 2, true));
    }
}
