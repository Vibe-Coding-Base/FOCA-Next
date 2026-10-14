using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foca.App.Views;
using Foca.Core.Model;
using Foca.Core.Reporting;
using Foca.Data;
using Foca.Plugins;
using Foca.Plugins.Hosting;

namespace Foca.App.ViewModels;

public sealed partial class PluginMenuItem : ObservableObject
{
    public PluginMenuItem(string title, IAsyncRelayCommand command)
    {
        Title = title;
        Command = command;
    }

    public string Title { get; }
    public IAsyncRelayCommand Command { get; }
}

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ProjectStore store;
    private readonly IDialogService dialogs;
    private readonly HashSet<string> knownDomains = new(StringComparer.OrdinalIgnoreCase);
    private PluginRegistry? plugins;

    public LogViewModel Log { get; }
    public ProjectPanelViewModel ProjectPanel { get; }
    public DocumentsPanelViewModel DocumentsPanel { get; }
    public SummaryPanelViewModel SummaryPanel { get; } = new();
    public DomainsPanelViewModel DomainsPanel { get; }

    public ObservableCollection<NavigationNode> Tree { get; } = new();
    public ObservableCollection<PluginMenuItem> PluginCommands { get; } = new();

    public List<Document> Documents { get; private set; } = new();
    public IReadOnlyCollection<string> KnownDomains => knownDomains;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle), nameof(HasProject))]
    private Project? currentProject;

    [ObservableProperty]
    private object? currentPanel;

    [ObservableProperty]
    private NavigationNode? selectedNode;

    public string WindowTitle => CurrentProject == null ? "FOCA Open Source" : $"{CurrentProject.Name} - FOCA Open Source";

    public bool HasProject => CurrentProject != null;

    public bool HasPluginCommands => PluginCommands.Count > 0;

    // ---- Appearance --------------------------------------------------------

    private readonly AppSettings settings;
    private readonly Action<AppTheme> applyTheme;

    public AppTheme Theme => settings.Theme;
    public bool IsSystemTheme => Theme == AppTheme.System;
    public bool IsLightTheme => Theme == AppTheme.Light;
    public bool IsDarkTheme => Theme == AppTheme.Dark;

    public string ThemeButtonText => Theme switch
    {
        AppTheme.Light => "☀ Light",
        AppTheme.Dark => "☾ Dark",
        _ => "◐ System",
    };

    /// <summary>The quick toggle in the menu bar: System -> Light -> Dark -> System.</summary>
    [RelayCommand]
    private void CycleTheme() => SetTheme(Theme switch
    {
        AppTheme.System => AppTheme.Light,
        AppTheme.Light => AppTheme.Dark,
        _ => AppTheme.System,
    });

    /// <summary>View > Theme: light, dark, or follow the operating system. Remembered across sessions.</summary>
    [RelayCommand]
    private void SetTheme(AppTheme theme)
    {
        settings.Theme = theme;
        applyTheme(theme);
        try
        {
            settings.Save();
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Log.Add("FOCA", LogSeverity.Low, "Settings could not be saved: " + e.Message);
        }
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ThemeButtonText));
    }

    public MainWindowViewModel(ProjectStore store, IDialogService dialogs, AppSettings? settings = null, Action<AppTheme>? applyTheme = null)
    {
        this.store = store;
        this.dialogs = dialogs;
        this.settings = settings ?? new AppSettings();
        this.applyTheme = applyTheme ?? (_ => { });
        Log = new LogViewModel(dialogs);
        ProjectPanel = new ProjectPanelViewModel(this, dialogs);
        DocumentsPanel = new DocumentsPanelViewModel(this, dialogs);
        DomainsPanel = new DomainsPanelViewModel(this, dialogs);
        PluginCommands.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPluginCommands));
        BuildTree();
    }

    public static string DefaultDownloadFolder(string projectName)
    {
        string safe = string.Concat(projectName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FOCA", "downloads", safe);
    }

    public async Task InitializeAsync()
    {
        try
        {
            await store.InitializeAsync();
            Log.Add("FOCA", LogSeverity.Low, "Database ready: " + store.Location);
        }
        catch (Exception e) when (e is System.Data.Common.DbException || e is IOException || e is InvalidOperationException)
        {
            Log.Add("FOCA", LogSeverity.Error, "Database could not be opened: " + e.Message);
            await dialogs.ShowAsync("Database not available", e.Message);
            return;
        }
        await LoadPluginsAsync(askApproval: false);
        await NewProjectAsync();
    }

    // ---- Project lifecycle -------------------------------------------------

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        ProjectPanel.LoadNew(await store.GetProjectsAsync());
        CurrentPanel = ProjectPanel;
    }

    [RelayCommand]
    private async Task EditProjectAsync()
    {
        if (CurrentProject == null)
        {
            await NewProjectAsync();
            return;
        }
        ProjectPanel.LoadExisting(CurrentProject, await store.GetProjectsAsync());
        CurrentPanel = ProjectPanel;
    }

    public async Task SaveProjectAsync(Project project, bool isNew)
    {
        if (isNew)
            project = await store.CreateProjectAsync(project);
        else
            await store.UpdateProjectAsync(project);
        Log.Add("FOCA", LogSeverity.Medium, $"Project '{project.Name}' saved ({project.Mode})");
        await OpenProjectAsync(project);
        if (isNew && !project.HasDomain && DocumentsPanel.AddFolderCommand.CanExecute(null))
            await DocumentsPanel.AddFolderCommand.ExecuteAsync(null);
    }

    public async Task OpenProjectAsync(Project project)
    {
        CurrentProject = project;
        knownDomains.Clear();
        Documents = (await store.GetDocumentsAsync(project.Id)).ToList();
        Log.Add("FOCA", LogSeverity.Low, $"Opened project '{project.Name}' with {Documents.Count} document(s)");
        plugins?.OpenProject(new DesktopProjectContext(this, project));
        RefreshViews();
        ShowDefaultPanel();
    }

    public async Task SaveDocumentsAsync(IReadOnlyCollection<Document> documents)
    {
        if (CurrentProject == null || documents.Count == 0)
            return;
        await store.SaveDocumentsAsync(CurrentProject.Id, documents);
        Documents = (await store.GetDocumentsAsync(CurrentProject.Id)).ToList();
        foreach (Document d in documents.Where(d => d.Status == DocumentStatus.Analyzed))
            plugins?.NotifyDocumentAnalyzed(d);
        RefreshViews();
    }

    public void AddKnownDomains(IEnumerable<string> domains)
    {
        foreach (string d in domains)
            knownDomains.Add(d.ToLowerInvariant());
    }

    public void ShowDefaultPanel()
    {
        if (CurrentProject == null)
            return;
        DocumentsPanel.Refresh();
        CurrentPanel = DocumentsPanel;
    }

    // ---- Tree --------------------------------------------------------------

    private void BuildTree()
    {
        Tree.Clear();
        var root = new NavigationNode(NodeKind.Project, CurrentProject?.Name ?? "Project Name");
        root.Children.Add(new NavigationNode(NodeKind.Network, "Network"));
        root.Children.Add(new NavigationNode(NodeKind.Domains, "Domains"));
        var analysis = new NavigationNode(NodeKind.DocumentAnalysis, "Document Analysis");
        analysis.Children.Add(new NavigationNode(NodeKind.Files, $"Files ({Documents.Count(d => d.Status != DocumentStatus.Failed || d.LocalPath != null)}/{Documents.Count})"));
        var summary = new NavigationNode(NodeKind.MetadataSummary, "Metadata Summary");
        foreach (IGrouping<FindingKind, SummaryEntry> g in MetadataSummary.Build(Documents).GroupBy(e => e.Kind))
            summary.Children.Add(new NavigationNode(NodeKind.SummaryKind, $"{g.Key} ({g.Count()})", g.Key));
        analysis.Children.Add(summary);
        root.Children.Add(analysis);
        Tree.Add(root);
    }

    private void RefreshViews()
    {
        BuildTree();
        DocumentsPanel.Refresh();
        DomainsPanel.Refresh();
        if (CurrentPanel == SummaryPanel)
            SummaryPanel.Show(MetadataSummary.Build(Documents), SelectedNode?.FindingKind);
    }

    partial void OnSelectedNodeChanged(NavigationNode? value)
    {
        if (value == null)
            return;
        switch (value.Kind)
        {
            case NodeKind.Project:
                _ = EditProjectAsync();
                break;
            case NodeKind.Network:
            case NodeKind.Domains:
                if (CurrentProject != null)
                {
                    DomainsPanel.Refresh();
                    CurrentPanel = DomainsPanel;
                }
                break;
            case NodeKind.MetadataSummary:
            case NodeKind.SummaryKind:
                SummaryPanel.Show(MetadataSummary.Build(Documents), value.FindingKind);
                CurrentPanel = SummaryPanel;
                break;
            default:
                ShowDefaultPanel();
                break;
        }
    }

    // ---- Reports -----------------------------------------------------------

    [RelayCommand]
    private async Task ExportReportAsync(string? format)
    {
        if (CurrentProject == null)
            return;
        ReportFormat f = Enum.Parse<ReportFormat>(format ?? "Html", ignoreCase: true);
        string ext = f.ToString().ToLowerInvariant();
        string? path = await dialogs.SaveFileAsync("Export report", CurrentProject.Name + "." + ext, ext);
        if (path == null)
            return;
        await using (var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)))
            ReportWriter.Write(writer, f, CurrentProject, Documents);
        Log.Add("FOCA", LogSeverity.Medium, "Report written to " + path);
    }

    // ---- Plugins -----------------------------------------------------------

    [RelayCommand]
    private Task ManagePluginsAsync() => LoadPluginsAsync(askApproval: true);

    private async Task LoadPluginsAsync(bool askApproval)
    {
        var approvals = new PluginApprovals(PluginApprovals.DefaultFile);
        var loader = new PluginLoader(PluginLoader.DefaultPluginsFolder, approvals);
        IReadOnlyList<DiscoveredPlugin> found = loader.Discover();
        if (askApproval)
        {
            if (found.Count == 0)
            {
                await dialogs.ShowAsync("Plugins", $"No plugins installed. Copy a plugin folder (with plugin.json) into {PluginLoader.DefaultPluginsFolder}.");
                return;
            }
            foreach (DiscoveredPlugin p in found.Where(p => p.State == PluginState.NeedsApproval))
            {
                string message = $"Load plugin '{p.Manifest!.Name}' {p.Manifest.Version} by {p.Manifest.Author ?? "unknown"}?\n\n" +
                                 $"{p.Manifest.Description}\n\nPlugins run with the same rights as FOCA. Only approve plugins you trust.\nSHA-256: {p.Sha256}";
                if (await dialogs.ConfirmAsync("Approve plugin", message))
                    approvals.Approve(p.Manifest.Id, p.Sha256!);
            }
            found = loader.Discover();
        }

        plugins = new PluginRegistry("desktop", (level, m) => Log.Add("Plugins", level == PluginLogLevel.Error ? LogSeverity.Error : LogSeverity.Low, m));
        foreach (DiscoveredPlugin p in found)
        {
            if (p.State != PluginState.Loaded)
            {
                if (p.State != PluginState.NeedsApproval)
                    Log.Add("Plugins", LogSeverity.Low, $"{p.Folder}: {p.State} {p.Error}");
                continue;
            }
            try
            {
                plugins.Initialize(loader.Load(p), p.Manifest!.Name);
            }
            catch (Exception e) when (e is InvalidOperationException || e is BadImageFormatException || e is FileLoadException)
            {
                Log.Add("Plugins", LogSeverity.Error, $"{p.Manifest?.Name}: {e.Message}");
            }
        }

        PluginCommands.Clear();
        foreach (PluginCommand command in plugins.Commands.Where(c => c.Placement == CommandPlacement.PluginsMenu))
        {
            PluginCommand c = command;
            PluginCommands.Add(new PluginMenuItem(c.Title, new AsyncRelayCommand(async () =>
            {
                var context = new CommandContext { Project = CurrentProject == null ? null : new DesktopProjectContext(this, CurrentProject) };
                if (!(c.IsAvailable?.Invoke(context) ?? true))
                    return;
                try
                {
                    await c.Execute(context, CancellationToken.None);
                }
                catch (Exception e)
                {
                    Log.Add("Plugins", LogSeverity.Error, $"{c.Title}: {e.Message}");
                }
            })));
        }
        if (CurrentProject != null)
            plugins.OpenProject(new DesktopProjectContext(this, CurrentProject));
    }

    [RelayCommand]
    private Task AboutAsync() => dialogs.ShowAsync("About FOCA",
        "FOCA Open Source - Fingerprinting Organizations with Collected Archives.\n\n" +
        "Finds metadata and hidden information in documents. Runs on Windows, Linux and macOS.\n" +
        "Licensed under the GNU GPL v3.");

    private sealed class DesktopProjectContext : IProjectContext
    {
        private readonly MainWindowViewModel main;

        public DesktopProjectContext(MainWindowViewModel main, Project project)
        {
            this.main = main;
            Project = project;
        }

        public Project Project { get; }

        public Task<IReadOnlyList<Document>> GetDocumentsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Document>>(main.Documents.ToList());

        public Task AddDocumentsAsync(IEnumerable<string> sources, CancellationToken cancellationToken = default) =>
            main.SaveDocumentsAsync(sources.Where(s => !main.Documents.Any(d => d.Source == s))
                .Select(s => new Document { Source = s, Extension = Path.GetExtension(s).ToLowerInvariant(), Status = DocumentStatus.Discovered }).ToList());

        public Task AddDomainsAsync(IEnumerable<string> domains, CancellationToken cancellationToken = default)
        {
            main.AddKnownDomains(domains);
            main.DomainsPanel.Refresh();
            return Task.CompletedTask;
        }
    }
}
