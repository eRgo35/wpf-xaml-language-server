using WpfXaml.Analysis.Features;
using WpfXaml.Analysis.Resources;
using WpfXaml.Analysis.Semantics;
using WpfXaml.Analysis.TypeSystem;
using WpfXaml.Language.Syntax;

namespace WpfXaml.Analysis.Tests;

public class CodeBehindNameTests
{
    private const string CodeBehindPath = "C:/TestApp/MainWindow.xaml.cs";

    private const string CodeBehind = """
        using System.Windows;

        namespace TestApp
        {
            public partial class MainWindow : Window
            {
                private void OnLoaded(object sender, RoutedEventArgs e)
                {
                    okButton.IsEnabled = false;
                    this.okButton.Focus();
                }

                private void Other()
                {
                    var okButton = 1;
                    okButton++;
                }
            }
        }
        """;

    private static readonly Lazy<XamlTypeSystem> Types = new(() => TestWorkspace.CreateTypeSystem(CodeBehind));

    private static (XamlSemanticModel Model, int Offset) At(string bodyWithCaret, XamlTypeSystem? types = null)
    {
        _ = TestWorkspace.Project; // Skips when the WPF targeting pack is missing.
        var (text, offset) = TestWorkspace.Caret(ReferenceTests.Window(bodyWithCaret));
        var model = new XamlSemanticModel(XamlDocument.Parse(text), types ?? Types.Value, new XamlResourceContext(new ReferenceTests.NoFilesHost(), ReferenceTests.MainPath));
        return (model, offset);
    }

    private static int LineOf(string source, string text) =>
        source.Split('\n').Select((line, i) => (line, i)).First(l => l.line.Contains(text, StringComparison.Ordinal)).i;

    [Fact]
    public void CodeBehind_UnboundUseFound()
    {
        var (model, _) = At("<Button x:Name=\"ok|Button\" />");

        var uses = CodeBehindNames.FindUses(model, "okButton");

        Assert.All(uses, u => Assert.Equal(CodeBehindPath, u.FilePath));
        Assert.Equal([LineOf(CodeBehind, "okButton.IsEnabled"), LineOf(CodeBehind, "this.okButton")], uses.Select(u => u.Start.Line).Order());
    }

    [Fact]
    public void CodeBehind_SameNamedLocalIgnored()
    {
        var (model, _) = At("<Button x:Name=\"ok|Button\" />");

        var lines = CodeBehindNames.FindUses(model, "okButton").Select(u => u.Start.Line).ToList();

        Assert.DoesNotContain(LineOf(CodeBehind, "okButton++"), lines);
    }

    [Fact]
    public void CodeBehind_OtherMemberIgnored()
    {
        var types = TestWorkspace.CreateTypeSystem(CodeBehind.Replace("private void Other()", "public object okButton { get; set; }\n        private void Other()"));
        var (model, _) = At("<Button x:Name=\"ok|Button\" />", types);

        Assert.Empty(CodeBehindNames.FindUses(model, "okButton"));
    }

    [Fact]
    public void Rename_ElementNameAlsoEditsCodeBehind()
    {
        var (model, offset) = At("<Button x:Name=\"ok|Button\" />");

        var result = RenameService.Rename(model, offset, "saveButton", []);

        Assert.Null(result.Error);
        Assert.Single(result.Edits, e => e.FilePath is null);
        Assert.Equal(2, result.Edits.Count(e => e.FilePath == CodeBehindPath));
    }

    [Fact]
    public void References_TemplateNameDoesNotReachCodeBehind()
    {
        var (model, offset) = At("""
            <Button>
              <Button.Template>
                <ControlTemplate TargetType="Button"><Border x:Name="ok|Button" /></ControlTemplate>
              </Button.Template>
            </Button>
            """);

        Assert.All(ReferenceService.FindReferences(model, offset, [], includeDeclaration: true), l => Assert.Null(l.FilePath));
    }
}
