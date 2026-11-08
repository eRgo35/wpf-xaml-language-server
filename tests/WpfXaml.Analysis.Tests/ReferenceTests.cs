using WpfXaml.Analysis.Features;
using WpfXaml.Analysis.Resources;
using WpfXaml.Analysis.Semantics;
using WpfXaml.Language.Syntax;

namespace WpfXaml.Analysis.Tests;

public class ReferenceTests
{
    internal const string MainPath = "C:/TestApp/MainWindow.xaml";

    /// <summary>A host that knows no other files: enough for single-document resource scopes.</summary>
    internal sealed class NoFilesHost : IXamlResourceHost
    {
        public XamlDocument? GetDocument(string filePath) => null;

        public ResourceSourceResolution ResolveSource(string? fromFilePath, string source) => ResourceSourceResolution.Unknown("test");

        public IReadOnlyList<string>? GetApplicationDefinitions(string? filePath) => null;

        public IReadOnlySet<string>? GetAllResourceKeys() => null;
    }

    internal static XamlSemanticModel Model(string text, string path) =>
        new(XamlDocument.Parse(text), TestWorkspace.Project, new XamlResourceContext(new NoFilesHost(), path));

    internal static string Window(string body) =>
        $"<Window {TestWorkspace.Header} x:Class=\"TestApp.MainWindow\">\n{body}\n</Window>";

    private static IReadOnlyList<XamlLocation> Find(string bodyWithCaret, bool includeDeclaration = true, params XamlSemanticModel[] others)
    {
        var (text, offset) = TestWorkspace.Caret(Window(bodyWithCaret));
        return ReferenceService.FindReferences(Model(text, MainPath), offset, others, includeDeclaration);
    }

    private const string AccentWindow = """
        <Window.Resources><SolidColorBrush x:Key="Accent" Color="Red" /></Window.Resources>
        <StackPanel>
          <Button Background="{StaticResource Acc|ent}" />
          <Border Background="{DynamicResource Accent}" />
        </StackPanel>
        """;

    [Fact]
    public void References_ResourceKeyAcrossUses()
    {
        Assert.Equal(3, Find(AccentWindow).Count);
        Assert.Equal(2, Find(AccentWindow, includeDeclaration: false).Count);
    }

    [Fact]
    public void References_ResourceKeyFromDefinition()
    {
        var locations = Find(AccentWindow.Replace("Acc|ent}", "Accent}").Replace("x:Key=\"Accent\"", "x:Key=\"Acc|ent\""));

        Assert.Equal(3, locations.Count);
        Assert.All(locations, l => Assert.Null(l.FilePath));
    }

    [Fact]
    public void References_ResourceKeyShadowedNotIncluded()
    {
        var locations = Find("""
            <Window.Resources><SolidColorBrush x:Key="Acc|ent" Color="Red" /></Window.Resources>
            <StackPanel>
              <Button Background="{StaticResource Accent}" />
              <Grid>
                <Grid.Resources><SolidColorBrush x:Key="Accent" Color="Blue" /></Grid.Resources>
                <Border Background="{StaticResource Accent}" />
              </Grid>
            </StackPanel>
            """, includeDeclaration: false);

        var only = Assert.Single(locations);
        Assert.Equal(3, only.Start.Line);
    }

    [Fact]
    public void References_ElementName()
    {
        var locations = Find("""
            <StackPanel>
              <CheckBox x:Name="ok" />
              <Button IsEnabled="{Binding IsChecked, ElementName=o|k}" />
            </StackPanel>
            """);

        Assert.Equal([2, 3], locations.Select(l => l.Start.Line).Order());
    }

    [Fact]
    public void References_NameUsesInTargetNameAndReference()
    {
        var locations = Find("""
            <StackPanel>
              <Border Name="o|k" />
              <ContentControl Content="{x:Reference ok}" />
              <Button>
                <Button.Triggers>
                  <EventTrigger RoutedEvent="Loaded">
                    <BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName="ok" Storyboard.TargetProperty="Opacity" To="1" /></Storyboard></BeginStoryboard>
                  </EventTrigger>
                </Button.Triggers>
              </Button>
            </StackPanel>
            """);

        Assert.Equal([2, 3, 7], locations.Select(l => l.Start.Line).Order());
    }

    [Fact]
    public void References_TemplateNamescopeSeparate()
    {
        var locations = Find("""
            <StackPanel>
              <Border x:Name="o|k" />
              <TextBlock Text="{Binding Tag, ElementName=ok}" />
              <Button>
                <Button.Template>
                  <ControlTemplate TargetType="Button">
                    <StackPanel>
                      <Border x:Name="ok" />
                      <TextBlock Text="{Binding Tag, ElementName=ok}" />
                    </StackPanel>
                  </ControlTemplate>
                </Button.Template>
              </Button>
            </StackPanel>
            """);

        Assert.Equal([2, 3], locations.Select(l => l.Start.Line).Order());
    }

    [Fact]
    public void References_TypeAcrossDocuments()
    {
        const string otherPath = "C:/TestApp/OtherWindow.xaml";
        var other = Model($"<UserControl {TestWorkspace.Header}>\n  <local:FancyButton Fanciness=\"1\" />\n</UserControl>", otherPath);

        var locations = Find("<local:Fancy|Button />", true, other);

        Assert.Equal(2, locations.Count);
        Assert.Contains(locations, l => l.FilePath is null && l.Start.Line == 1);
        Assert.Contains(locations, l => l.FilePath == otherPath && l.Start.Line == 1);
    }

    [Fact]
    public void References_MemberIncludesInheritedUses()
    {
        var locations = Find("""
            <StackPanel>
              <Button Mar|gin="1" />
              <Border Margin="2" />
              <Border Padding="2" />
            </StackPanel>
            """);

        Assert.Equal([2, 3], locations.Select(l => l.Start.Line).Order());
    }

    [Fact]
    public void References_BrokenMarkupNoThrow()
    {
        Assert.Empty(Find("<Grid>\n  <|\n</Grid>"));
    }
}
