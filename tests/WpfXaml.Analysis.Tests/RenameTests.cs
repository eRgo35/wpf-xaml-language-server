using WpfXaml.Analysis.Features;

namespace WpfXaml.Analysis.Tests;

public class RenameTests
{
    private static (Semantics.XamlSemanticModel Model, int Offset, string Text) At(string bodyWithCaret)
    {
        var (text, offset) = TestWorkspace.Caret(ReferenceTests.Window(bodyWithCaret));
        return (ReferenceTests.Model(text, ReferenceTests.MainPath), offset, text);
    }

    private const string AccentBody = """
        <Window.Resources><SolidColorBrush x:Key="Acc|ent" Color="Red" /></Window.Resources>
        <StackPanel>
          <Button Background="{StaticResource Accent}" />
          <Border Background="{DynamicResource Accent}" />
        </StackPanel>
        """;

    [Fact]
    public void Rename_ResourceKeyUpdatesAllUses()
    {
        var (model, offset, _) = At(AccentBody);

        var result = RenameService.Rename(model, offset, "Brand", []);

        Assert.Null(result.Error);
        Assert.Equal([1, 3, 4], result.Edits.Select(e => e.Start.Line).Order());
        Assert.All(result.Edits, e => Assert.Equal("Brand", e.NewText));
    }

    [Fact]
    public void Rename_ElementNameUpdatesBindings()
    {
        var (model, offset, _) = At("""
            <StackPanel>
              <CheckBox x:Name="o|k" />
              <Button IsEnabled="{Binding IsChecked, ElementName=ok}" />
            </StackPanel>
            """);

        var result = RenameService.Rename(model, offset, "accept", []);

        Assert.Null(result.Error);
        Assert.Equal([2, 3], result.Edits.Select(e => e.Start.Line).Order());
        Assert.All(result.Edits, e => Assert.Equal(2, e.End.Character - e.Start.Character));
    }

    [Fact]
    public void Rename_ToExistingNameRejected()
    {
        var (model, offset, _) = At("""
            <StackPanel>
              <CheckBox x:Name="o|k" />
              <Button x:Name="other" />
            </StackPanel>
            """);

        var result = RenameService.Rename(model, offset, "other", []);

        Assert.Empty(result.Edits);
        Assert.Equal("'other' is already used in this scope.", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a\"b")]
    [InlineData("a{b")]
    public void Rename_InvalidKeysRejected(string newName)
    {
        var (model, offset, _) = At(AccentBody);

        var result = RenameService.Rename(model, offset, newName, []);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Edits);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("1ok")]
    [InlineData("class")]
    public void Rename_InvalidElementNamesRejected(string newName)
    {
        var (model, offset, _) = At("<CheckBox x:Name=\"o|k\" />");

        var result = RenameService.Rename(model, offset, newName, []);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Edits);
    }

    [Fact]
    public void PrepareRename_ReturnsNameSpan()
    {
        var (model, offset, text) = At("<CheckBox x:Name=\"o|k\" />");

        var span = RenameService.PrepareRename(model, offset);

        Assert.NotNull(span);
        Assert.Equal("ok", text[span.Value.Start..span.Value.End]);
    }

    [Fact]
    public void PrepareRename_OnTypeNameReturnsNull()
    {
        var (model, offset, _) = At("<But|ton />");

        Assert.Null(RenameService.PrepareRename(model, offset));
    }

    [Fact]
    public void Rename_BrokenMarkupReturnsError()
    {
        var (model, offset, _) = At("<Grid>\n  <|\n</Grid>");

        Assert.NotNull(RenameService.Rename(model, offset, "x", []).Error);
    }
}
