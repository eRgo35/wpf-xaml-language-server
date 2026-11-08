using System.Text.Json;
using WpfXaml.Analysis.Framework;
using WpfXaml.LanguageServer.Protocol;

namespace WpfXaml.LanguageServer.Tests;

/// <summary>Protocol-level tests in framework-only mode (no MSBuild).</summary>
public class LanguageServerTests
{
    private const string Uri = "file:///C:/Test/MainWindow.xaml";

    private const string Document = """
        <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Grid>
            <Button Contnet="OK" Grid.Row="1" />
          </Grid>
        </Window>
        """;

    private static Task<TestClient> StartAsync()
    {
        Assert.SkipWhen(FrameworkReferences.FindWpfReferencePaths().Count == 0, "No WindowsDesktop targeting pack found.");
        return TestClient.StartAsync(new { loadProjects = false });
    }

    [Fact]
    public async Task Initialize_AdvertisesCapabilities()
    {
        var (clientStream, serverStream) = Nerdbank.Streams.FullDuplexStream.CreatePair();
        var server = XamlLanguageServer.Create(serverStream, serverStream, new ServerLog(LogLevel.Error));

        var result = server.Initialize(new InitializeParams(null, null, null, null, null, null));

        Assert.True(result.Capabilities.HoverProvider);
        Assert.True(result.Capabilities.DefinitionProvider);
        Assert.True(result.Capabilities.DocumentSymbolProvider);
        Assert.True(result.Capabilities.ReferencesProvider);
        Assert.Equal(TextDocumentSyncKind.Incremental, result.Capabilities.TextDocumentSync!.Change);
        Assert.Contains("<", result.Capabilities.CompletionProvider!.TriggerCharacters);
        ((IDisposable)server).Dispose();
        await clientStream.DisposeAsync();
    }

    [Fact]
    public async Task DidOpen_PublishesDiagnostics()
    {
        await using var client = await StartAsync();
        await client.OpenAsync(Uri, Document);

        var published = await client.WaitForDiagnosticsAsync(Uri, d => d.Diagnostics.Length > 0);

        var diagnostic = Assert.Single(published.Diagnostics);
        Assert.Equal("XLS1003", diagnostic.Code);
        Assert.Equal(new Position(3, 12), diagnostic.Range.Start);
        Assert.Contains("Did you mean 'Content'?", diagnostic.Message);
    }

    [Fact]
    public async Task DidChange_IncrementalEdit_UpdatesDiagnostics()
    {
        await using var client = await StartAsync();
        await client.OpenAsync(Uri, Document);
        await client.WaitForDiagnosticsAsync(Uri, d => d.Diagnostics.Length == 1);

        // Fix the typo: "Contnet" -> "Content" by replacing the 7 characters at line 3, col 12.
        var edit = new TextDocumentContentChangeEvent(new Protocol.Range(new Position(3, 12), new Position(3, 19)), "Content");
        await client.NotifyAsync("textDocument/didChange",
            new DidChangeTextDocumentParams(new VersionedTextDocumentIdentifier(Uri, 2), [edit]));

        var published = await client.WaitForDiagnosticsAsync(Uri, d => d.Version == 2);
        Assert.Empty(published.Diagnostics);
    }

    [Fact]
    public async Task Completion_ReturnsAttributesWithTextEdits()
    {
        await using var client = await StartAsync();
        await client.OpenAsync(Uri, Document);

        // Caret after `Grid.Row="1" ` (line 3, col 38): a new attribute.
        var list = await client.RequestAsync<CompletionList>("textDocument/completion", TestClient.Position(Uri, 3, 38));

        var content = Assert.Single(list.Items, i => i.Label == "Content");
        Assert.Equal(InsertTextFormat.Snippet, content.InsertTextFormat);
        Assert.Equal("Content=\"$1\"", content.TextEdit!.NewText);
        Assert.NotNull(content.Data?.SymbolId);

        var resolved = await client.RequestAsync<CompletionItem>("completionItem/resolve", content);
        Assert.False(string.IsNullOrEmpty(resolved.Documentation?.Value));
    }

    [Fact]
    public async Task Hover_ReturnsMarkdown()
    {
        await using var client = await StartAsync();
        await client.OpenAsync(Uri, Document);

        var hover = await client.RequestAsync<Hover>("textDocument/hover", TestClient.Position(Uri, 3, 7));

        Assert.Equal("markdown", hover.Contents.Kind);
        Assert.Contains("System.Windows.Controls.Button", hover.Contents.Value);
    }

    [Fact]
    public async Task Hover_OnNothing_ReturnsNull()
    {
        await using var client = await StartAsync();
        await client.OpenAsync(Uri, Document);

        var hover = await client.RequestAsync<JsonElement>("textDocument/hover", TestClient.Position(Uri, 2, 0));

        Assert.Equal(JsonValueKind.Null, hover.ValueKind);
    }

    [Fact]
    public async Task DocumentSymbol_ReturnsHierarchy()
    {
        await using var client = await StartAsync();
        await client.OpenAsync(Uri, Document);

        var symbols = await client.RequestAsync<JsonElement>("textDocument/documentSymbol", new { textDocument = new { uri = Uri } });

        var root = Assert.Single(symbols.EnumerateArray());
        Assert.Equal("Window", root.GetProperty("name").GetString());
        Assert.Equal(5, root.GetProperty("kind").GetInt32());
        var grid = Assert.Single(root.GetProperty("children").EnumerateArray());
        Assert.Equal("Grid", grid.GetProperty("name").GetString());
        Assert.Equal(2, grid.GetProperty("selectionRange").GetProperty("start").GetProperty("line").GetInt32());
    }

    private const string NamedDocument = """
        <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <StackPanel>
            <CheckBox x:Name="ok" />
            <Button IsEnabled="{Binding IsChecked, ElementName=ok}" />
          </StackPanel>
        </Window>
        """;

    [Fact]
    public async Task References_ReturnsLocations()
    {
        await using var client = await StartAsync();
        await client.OpenAsync(Uri, NamedDocument);

        // Caret on `ok` in x:Name="ok" (line 3).
        var request = new { textDocument = new { uri = Uri }, position = new { line = 3, character = 24 }, context = new { includeDeclaration = true } };
        var locations = await client.RequestAsync<Location[]>("textDocument/references", request);

        Assert.Equal([3, 4], locations.Select(l => l.Range.Start.Line).Order());
        Assert.All(locations, l => Assert.Equal(Uri, l.Uri));

        var withoutDeclaration = await client.RequestAsync<Location[]>("textDocument/references", request with { context = new { includeDeclaration = false } });
        Assert.Equal(4, Assert.Single(withoutDeclaration).Range.Start.Line);
    }
}
