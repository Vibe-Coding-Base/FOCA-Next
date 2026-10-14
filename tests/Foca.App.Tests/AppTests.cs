using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Foca.App;
using Foca.App.ViewModels;
using Foca.App.Views;
using Foca.Core.Model;
using Foca.Data;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Foca.App.Tests.TestApp))]

namespace Foca.App.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>Scripted dialogs: answers are queued by the test.</summary>
internal sealed class FakeDialogs : IDialogService
{
    public Queue<string?> Folders { get; } = new();
    public Queue<bool> Confirmations { get; } = new();
    public List<string> Messages { get; } = new();

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folders.Count > 0 ? Folders.Dequeue() : null);
    public Task<IReadOnlyList<string>> PickFilesAsync(string title) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    public Task<string?> SaveFileAsync(string title, string suggestedName, string extension) => Task.FromResult<string?>(null);
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(Confirmations.Count > 0 && Confirmations.Dequeue());

    public Task ShowAsync(string title, string message)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class AppTests : IDisposable
{
    private readonly string temp = Directory.CreateTempSubdirectory("foca-app").FullName;

    private static string Corpus([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "corpus"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
    }

    private (MainWindowViewModel Vm, FakeDialogs Dialogs, AppSettings Settings, List<AppTheme> Applied) Create()
    {
        var dialogs = new FakeDialogs();
        AppSettings settings = AppSettings.Load(Path.Combine(temp, "settings.json"));
        var applied = new List<AppTheme>();
        var vm = new MainWindowViewModel(ProjectStore.OpenSqlite(Path.Combine(temp, "foca.db")), dialogs, settings, applied.Add);
        return (vm, dialogs, settings, applied);
    }

    [AvaloniaFact]
    public void ThemeButtonCyclesAndIsRemembered()
    {
        var (vm, _, _, applied) = Create();
        Assert.Equal(AppTheme.System, vm.Theme);

        vm.CycleThemeCommand.Execute(null);
        vm.CycleThemeCommand.Execute(null);
        Assert.Equal(AppTheme.Dark, vm.Theme);
        Assert.True(vm.IsDarkTheme);
        Assert.Equal(new[] { AppTheme.Light, AppTheme.Dark }, applied);

        Assert.Equal(AppTheme.Dark, AppSettings.Load(Path.Combine(temp, "settings.json")).Theme);

        vm.SetThemeCommand.Execute(AppTheme.System);
        Assert.Equal(AppTheme.System, AppSettings.Load(Path.Combine(temp, "settings.json")).Theme);
    }

    [AvaloniaFact]
    public void CorruptSettingsFallBackToDefaults()
    {
        string file = Path.Combine(temp, "bad.json");
        File.WriteAllText(file, "{ not json");
        Assert.Equal(AppTheme.System, AppSettings.Load(file).Theme);
    }

    [AvaloniaFact]
    public void ApiKeysAreStoredEncryptedAndRoundTrip()
    {
        string file = Path.Combine(temp, "creds.json");
        AppSettings s = AppSettings.Load(file);
        s.Credentials.GoogleApiKey = "super-secret-google-key";
        s.Credentials.GoogleCx = "cx123";
        s.Credentials.ShodanApiKey = "shodan-key";
        s.Save();

        string onDisk = File.ReadAllText(file);
        Assert.DoesNotContain("super-secret-google-key", onDisk);
        Assert.DoesNotContain("shodan-key", onDisk);

        AppSettings reloaded = AppSettings.Load(file);
        Assert.Equal("super-secret-google-key", reloaded.Credentials.GoogleApiKey);
        Assert.Equal("shodan-key", reloaded.Credentials.ShodanApiKey);
        Assert.True(reloaded.Credentials.HasGoogleApi);
        Assert.True(reloaded.Credentials.HasShodan);
        Assert.False(reloaded.Credentials.HasBingApi);
    }

    [AvaloniaFact]
    public async Task ApiEnginesAppearOnlyWhenKeysAreConfigured()
    {
        var (vm, _, _, _) = Create();
        await vm.InitializeAsync();
        Assert.DoesNotContain(vm.DocumentsPanel.Engines, e => e.Name == "Google API");

        vm.Credentials.GoogleApiKey = "k";
        vm.Credentials.GoogleCx = "cx";
        vm.RefreshSearchEngines();
        Assert.Contains(vm.DocumentsPanel.Engines, e => e.Name == "Google API");
    }

    [AvaloniaFact]
    public async Task DocumentAnalysisProjectImportsAFolderWithoutADomain()
    {
        var (vm, dialogs, _, _) = Create();
        await vm.InitializeAsync();
        Assert.Same(vm.ProjectPanel, vm.CurrentPanel);

        dialogs.Folders.Enqueue(Corpus());   // folder picked right after creation
        vm.ProjectPanel.IsDocumentMode = true;
        vm.ProjectPanel.Name = "Leaked docs";
        await vm.ProjectPanel.SubmitCommand.ExecuteAsync(null);

        Assert.NotNull(vm.CurrentProject);
        Assert.False(vm.CurrentProject!.HasDomain);
        Assert.Same(vm.DocumentsPanel, vm.CurrentPanel);
        Assert.Equal(7, vm.Documents.Count);
        Assert.False(vm.DocumentsPanel.CanSearch);

        // Web search explains that a domain is needed instead of searching
        await vm.DocumentsPanel.SearchAllCommand.ExecuteAsync(null);
        Assert.Contains(Project.DomainRequiredMessage, dialogs.Messages);

        // The tree lists the summary groups like the original FOCA
        NavigationNode summary = vm.Tree[0].Children.Single(n => n.Kind == NodeKind.DocumentAnalysis).Children.Single(n => n.Kind == NodeKind.MetadataSummary);
        NavigationNode users = summary.Children.Single(n => n.FindingKind == FindingKind.User);
        vm.SelectedNode = users;
        Assert.Same(vm.SummaryPanel, vm.CurrentPanel);
        Assert.Contains(vm.SummaryPanel.Rows, r => r.Value == "alice.author");
        Assert.All(vm.SummaryPanel.Rows, r => Assert.Equal("User", r.Kind));
    }

    [AvaloniaFact]
    public async Task DomainCanBeAddedLater()
    {
        var (vm, dialogs, _, _) = Create();
        await vm.InitializeAsync();
        vm.ProjectPanel.IsDocumentMode = true;
        vm.ProjectPanel.Name = "Later";
        await vm.ProjectPanel.SubmitCommand.ExecuteAsync(null);   // no folder picked
        Assert.False(vm.CurrentProject!.HasDomain);

        await vm.EditProjectCommand.ExecuteAsync(null);
        Assert.True(vm.ProjectPanel.IsEditing);
        vm.ProjectPanel.IsDomainMode = true;
        vm.ProjectPanel.Domain = "Acme.com";
        await vm.ProjectPanel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("acme.com", vm.CurrentProject!.Domain);
        Assert.True(vm.DocumentsPanel.CanSearch);
        Assert.StartsWith("site:acme.com (", vm.DocumentsPanel.SearchText);
    }

    [AvaloniaFact]
    public async Task InvalidDomainIsRejected()
    {
        var (vm, _, _, _) = Create();
        await vm.InitializeAsync();
        vm.ProjectPanel.Domain = "not a domain";
        await vm.ProjectPanel.SubmitCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ProjectPanel.Error);
        Assert.Null(vm.CurrentProject);
    }

    [AvaloniaFact]
    public async Task MainWindowRendersEveryPanel()
    {
        var (vm, dialogs, _, _) = Create();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        await vm.InitializeAsync();
        dialogs.Folders.Enqueue(Corpus());
        vm.ProjectPanel.IsDocumentMode = true;
        vm.ProjectPanel.Name = "Render";
        await vm.ProjectPanel.SubmitCommand.ExecuteAsync(null);

        vm.ShowOptionsCommand.Execute(null);
        var options = vm.CurrentPanel;

        var expected = new (object Panel, Type View)[]
        {
            (vm.ProjectPanel, typeof(ProjectPanelView)),
            (vm.DocumentsPanel, typeof(DocumentsPanelView)),
            (vm.SummaryPanel, typeof(SummaryPanelView)),
            (vm.DomainsPanel, typeof(DomainsPanelView)),
            (options!, typeof(OptionsPanelView)),
        };
        foreach ((object panel, Type view) in expected)
        {
            vm.CurrentPanel = panel;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            // The data template produced the view and it was laid out with a real size
            Control shown = Assert.Single(window.GetVisualDescendants().OfType<Control>(), c => c.GetType() == view);
            Assert.True(shown.Bounds.Width > 0 && shown.Bounds.Height > 0, view.Name + " has no size");
        }
        Assert.Equal("Render - FOCA Next", window.Title);
        window.Close();
    }
}
