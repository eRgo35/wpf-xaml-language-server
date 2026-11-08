using WpfXaml.Analysis.Features;
using WpfXaml.Language.Syntax;

namespace WpfXaml.Analysis.Tests;

public class DocumentSymbolTests
{
    private static IReadOnlyList<XamlSymbol> Symbols(string xaml) => DocumentSymbolService.GetSymbols(XamlDocument.Parse(xaml));

    [Fact]
    public void Symbols_NestedTree()
    {
        var root = Assert.Single(Symbols("<Window><Grid><Button x:Name=\"ok\"/></Grid></Window>"));

        Assert.Equal("Window", root.Name);
        var grid = Assert.Single(root.Children);
        Assert.Equal("Grid", grid.Name);
        Assert.Equal("Button ok", Assert.Single(grid.Children).Name);
    }

    [Fact]
    public void Symbols_SpansCoverElementAndName()
    {
        const string xaml = "<Window><Grid /></Window>";
        var grid = Assert.Single(Assert.Single(Symbols(xaml)).Children);

        Assert.Equal("<Grid />", xaml[grid.Span.Start..grid.Span.End]);
        Assert.Equal("Grid", xaml[grid.NameSpan.Start..grid.NameSpan.End]);
    }

    [Fact]
    public void Symbols_UsesKeyOrNameAttribute()
    {
        var children = Assert.Single(Symbols(
            "<Grid><SolidColorBrush x:Key=\"Accent\"/><Button Name=\"b\"/><Button x:Name=\"n\" Name=\"m\" x:Key=\"k\"/><Button Name=\"m\" x:Key=\"k\"/></Grid>")).Children;

        Assert.Equal(["SolidColorBrush Accent", "Button b", "Button n", "Button m"], children.Select(c => c.Name));
    }

    [Fact]
    public void Symbols_PropertyElementsFlagged()
    {
        var children = Assert.Single(Symbols("<Grid><Grid.RowDefinitions><RowDefinition/></Grid.RowDefinitions><Button/></Grid>")).Children;

        Assert.True(children[0].IsPropertyElement);
        Assert.Equal("Grid.RowDefinitions", children[0].Name);
        Assert.False(children[1].IsPropertyElement);
    }

    [Fact]
    public void Symbols_EmptyNameElementSkipped()
    {
        var root = Assert.Single(Symbols("<Grid>\n  <\n</Grid>"));

        Assert.Empty(root.Children);
    }
}
