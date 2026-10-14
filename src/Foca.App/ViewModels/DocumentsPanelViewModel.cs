using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foca.App.Views;
using Foca.Core.Analysis;
using Foca.Core.Model;
using Foca.Core.Search;
using Foca.Core.Web;

namespace Foca.App.ViewModels;

public sealed partial class SelectableOption : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool isSelected;

    public SelectableOption(string name, bool isSelected)
    {
        Name = name;
        this.isSelected = isSelected;
    }
}

/// <summary>One row of the documents list.</summary>
public sealed class DocumentRow
{
    public DocumentRow(int index, Document document)
    {
        Index = index;
        Document = document;
    }

    public int Index { get; }
    public Document Document { get; }
    public string Type => Document.Extension.TrimStart('.');
    public string Url => Document.Source;
    public string Downloaded => Document.Status is DocumentStatus.Discovered ? "-" : Document.Status == DocumentStatus.Failed && Document.LocalPath == null ? "×" : "•";
    public string DownloadDate => Document.AnalyzedAt?.ToLocalTime().ToString("g") ?? Document.DiscoveredAt.ToLocalTime().ToString("g");
    public string Size => Document.Size == 0 ? "" : Document.Size < 1024 * 1024 ? $"{Document.Size / 1024.0:0.#} KB" : $"{Document.Size / 1024.0 / 1024.0:0.#} MB";
    public string Analyzed => Document.Status switch
    {
        DocumentStatus.Analyzed => $"• {Document.Findings.Count}",
        DocumentStatus.Failed => "error: " + Document.Error,
        _ => "",
    };
}

/// <summary>Search, download and metadata extraction. Mirrors PanelMetadataSearch of the WinForms app.</summary>
public sealed partial class DocumentsPanelViewModel : ObservableObject
{
    private static readonly string[] Extensions = { "doc", "docx", "sxw", "odt", "ppt", "pptx", "odp", "pps", "ppsx", "xls", "xlsx", "ods", "odg", "pdf", "wpd", "rtf", "svg", "indd", "rdp", "ica" };

    private readonly MainWindowViewModel main;
    private readonly IDialogService dialogs;
    private CancellationTokenSource? work;

    public ObservableCollection<SelectableOption> Engines { get; } = new()
    {
        new("Google", false),
        new("Bing", true),
        new("DuckDuckGo", true),
        new("Wayback Machine", true),
    };

    /// <summary>Adds or removes the API-based engines depending on which keys are configured.</summary>
    public void RefreshEngines()
    {
        void Toggle(string name, bool present)
        {
            bool exists = Engines.Any(e => e.Name == name);
            if (present && !exists)
                Engines.Add(new SelectableOption(name, false));
            else if (!present && exists)
                Engines.Remove(Engines.First(e => e.Name == name));
        }
        Toggle("Google API", main.Credentials.HasGoogleApi);
        Toggle("Bing API", main.Credentials.HasBingApi);
    }

    public ObservableCollection<SelectableOption> FileTypes { get; } = new(Extensions.Select(e => new SelectableOption(e, e is "doc" or "docx" or "pdf" or "xls" or "xlsx" or "ppt" or "pptx" or "odt")));

    public ObservableCollection<DocumentRow> Rows { get; } = new();

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ExtractAllCommand), nameof(DownloadAllCommand), nameof(AddFilesCommand), nameof(AddFolderCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string status = "";

    [ObservableProperty]
    private double progress;

    public string SearchButtonText => IsBusy ? "Stop" : "Search All";

    public bool CanSearch => main.CurrentProject?.HasDomain == true;

    public string SearchHint => CanSearch ? "" : Project.DomainRequiredMessage;

    public DocumentsPanelViewModel(MainWindowViewModel main, IDialogService dialogs)
    {
        this.main = main;
        this.dialogs = dialogs;
        foreach (SelectableOption type in FileTypes)
            type.PropertyChanged += (_, _) => UpdateSearchText();
    }

    public void Refresh()
    {
        Rows.Clear();
        int i = 0;
        foreach (Document d in main.Documents)
            Rows.Add(new DocumentRow(i++, d));
        UpdateSearchText();
        OnPropertyChanged(nameof(CanSearch));
        OnPropertyChanged(nameof(SearchHint));
    }

    private IReadOnlyList<string> SelectedExtensions() => FileTypes.Where(f => f.IsSelected).Select(f => f.Name).ToList();

    private void UpdateSearchText()
    {
        Project? p = main.CurrentProject;
        SearchText = p?.HasDomain == true
            ? $"site:{p.Domain} (" + string.Join(" OR ", SelectedExtensions().Select(e => "filetype:" + e)) + ")"
            : "";
    }

    [RelayCommand]
    private void SelectAllTypes(bool selected)
    {
        foreach (SelectableOption o in FileTypes)
            o.IsSelected = selected;
        UpdateSearchText();
    }

    [RelayCommand]
    private async Task SearchAllAsync()
    {
        if (IsBusy)
        {
            work?.Cancel();
            return;
        }
        Project? project = main.CurrentProject;
        if (project == null || !project.HasDomain)
        {
            await dialogs.ShowAsync("Domain required", Project.DomainRequiredMessage);
            return;
        }
        IReadOnlyList<string> extensions = SelectedExtensions();
        if (extensions.Count == 0 || !Engines.Any(e => e.IsSelected))
        {
            await dialogs.ShowAsync("Search", "Select at least one search engine and one extension.");
            return;
        }

        await RunAsync("Searching", async ct =>
        {
            using HttpClient services = TargetHttpClient.ForServices();
            var known = new HashSet<string>(main.Documents.Select(d => d.Source), StringComparer.OrdinalIgnoreCase);
            var found = new List<Document>();
            foreach (IDocumentSearchProvider provider in CreateProviders(services))
            {
                foreach (string domain in new[] { project.Domain! }.Concat(project.AlternativeDomains))
                {
                    main.Log.Add("MetadataSearch", LogSeverity.Low, $"Searching {domain} with {provider.Name}");
                    try
                    {
                        await foreach (Uri uri in provider.SearchAsync(domain, extensions, ct))
                        {
                            if (!known.Add(uri.AbsoluteUri))
                                continue;
                            var doc = new Document { ProjectId = project.Id, Source = uri.AbsoluteUri, Extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant(), Status = DocumentStatus.Discovered };
                            found.Add(doc);
                            Rows.Add(new DocumentRow(Rows.Count, doc));
                            Status = $"{found.Count} new document(s)";
                        }
                    }
                    catch (Exception e) when (e is HttpRequestException || e is InvalidOperationException || e is ArgumentException || e is System.Text.Json.JsonException)
                    {
                        // One engine failing (blocked, retired API) must not stop the others
                        main.Log.Add("MetadataSearch", LogSeverity.Error, $"{provider.Name}: {e.Message}");
                    }
                }
            }
            await main.SaveDocumentsAsync(found);
            main.Log.Add("MetadataSearch", LogSeverity.Medium, $"Search finished: {found.Count} new document(s)");
        });
    }

    private IEnumerable<IDocumentSearchProvider> CreateProviders(HttpClient services)
    {
        foreach (SelectableOption engine in Engines.Where(e => e.IsSelected))
        {
            ApiCredentials creds = main.Credentials;
            IDocumentSearchProvider provider = engine.Name switch
            {
                "Google" => SearchEngineProvider.Create(SearchEngine.GoogleWeb),
                "Bing" => SearchEngineProvider.Create(SearchEngine.BingWeb),
                "DuckDuckGo" => SearchEngineProvider.Create(SearchEngine.DuckDuckGo),
                "Google API" => SearchEngineProvider.Create(SearchEngine.GoogleApi, creds.GoogleApiKey, creds.GoogleCx),
                "Bing API" => SearchEngineProvider.Create(SearchEngine.BingApi, creds.BingApiKey),
                _ => new WaybackProvider(services),
            };
            if (provider is SearchEngineProvider s)
                s.Log += m => main.Log.Add("MetadataSearch", LogSeverity.Debug, m);
            yield return provider;
        }
    }

    private bool CanWork() => !IsBusy && main.CurrentProject != null;

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task AddFilesAsync()
    {
        IReadOnlyList<string> files = await dialogs.PickFilesAsync("Add documents");
        await AnalyzeLocalAsync(files.SelectMany(f => DocumentScanner.Scan(f, recursive: false)).ToList());
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task AddFolderAsync()
    {
        string? folder = await dialogs.PickFolderAsync("Select the folder with the documents to analyze");
        if (folder == null)
            return;
        bool recursive = Directory.EnumerateDirectories(folder).Any()
            && await dialogs.ConfirmAsync("Add folder", $"Include documents in the subfolders of {folder}?");
        var files = DocumentScanner.Scan(folder, recursive, (dir, e) => main.Log.Add("MetadataSearch", LogSeverity.Debug, $"Skipping folder {dir}: {e.Message}")).ToList();
        if (files.Count == 0)
        {
            await dialogs.ShowAsync("Add folder", $"No supported documents were found in {folder}.");
            return;
        }
        await AnalyzeLocalAsync(files);
    }

    private async Task AnalyzeLocalAsync(IReadOnlyList<ScannedFile> files)
    {
        if (files.Count == 0 || main.CurrentProject == null)
            return;
        int projectId = main.CurrentProject.Id;
        await RunAsync("Extracting metadata", async ct =>
        {
            var progress = new Progress<AnalysisProgress>(p =>
            {
                Progress = 100.0 * p.Completed / p.Total;
                Status = $"{p.Completed}/{p.Total} {Path.GetFileName(p.CurrentPath)}";
            });
            IReadOnlyList<Document> docs = await AnalysisPipeline.AnalyzeAsync(files, projectId, null, progress, ct);
            await main.SaveDocumentsAsync(docs);
            main.Log.Add("MetadataSearch", LogSeverity.Medium, $"Added {docs.Count} document(s), {docs.Count(d => d.Status == DocumentStatus.Failed)} failed");
        });
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task DownloadAllAsync()
    {
        Project project = main.CurrentProject!;
        List<Document> pending = main.Documents.Where(d => d.Status == DocumentStatus.Discovered).ToList();
        if (pending.Count == 0)
        {
            await dialogs.ShowAsync("Download", "There are no documents to download.");
            return;
        }
        await RunAsync("Downloading", async ct =>
        {
            string folder = string.IsNullOrWhiteSpace(project.DownloadFolder) ? MainWindowViewModel.DefaultDownloadFolder(project.Name) : project.DownloadFolder;
            using HttpClient targets = TargetHttpClient.ForTargets();
            using HttpClient services = TargetHttpClient.ForServices();
            var toTarget = new DocumentDownloader(targets, folder);
            var toArchive = new DocumentDownloader(services, folder);
            int done = 0;
            var downloaded = new List<Document>();
            await Parallel.ForEachAsync(pending, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (doc, token) =>
            {
                var url = new Uri(doc.Source);
                DownloadResult r = await (WaybackProvider.OriginalOf(url) != null ? toArchive : toTarget).DownloadAsync(url, token);
                lock (downloaded)
                {
                    if (r.Succeeded)
                    {
                        doc.LocalPath = r.LocalPath;
                        doc.Extension = r.Extension!;
                        doc.Size = r.Size;
                        doc.Sha256 = r.Sha256;
                        doc.Status = DocumentStatus.Downloaded;
                        downloaded.Add(doc);
                    }
                    else
                    {
                        doc.Status = DocumentStatus.Failed;
                        doc.Error = r.Error;
                    }
                }
                main.Log.Add("MetadataSearch", r.Succeeded ? LogSeverity.Debug : LogSeverity.Low, $"{(r.Succeeded ? "Downloaded" : "Failed")} {doc.Source} {r.Error}");
                int n = Interlocked.Increment(ref done);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { Progress = 100.0 * n / pending.Count; Status = $"{n}/{pending.Count} downloaded"; });
            });

            IReadOnlyList<Document> analyzed = await AnalysisPipeline.AnalyzeAsync(
                downloaded.Select(d => new ScannedFile(d.LocalPath!, d.Extension, d.Size)).ToList(), project.Id, cancellationToken: ct);
            for (int i = 0; i < downloaded.Count; i++)
                analyzed[i].Source = downloaded[i].Source;
            await main.SaveDocumentsAsync(pending.Where(d => d.Status == DocumentStatus.Failed).Concat(analyzed).ToList());
        });
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task ExtractAllAsync()
    {
        List<ScannedFile> files = main.Documents
            .Where(d => d.LocalPath != null && File.Exists(d.LocalPath) && DocumentAnalyzer.IsSupported(d.Extension))
            .Select(d => new ScannedFile(d.LocalPath!, d.Extension, d.Size)).ToList();
        Dictionary<string, string> sources = main.Documents.Where(d => d.LocalPath != null).GroupBy(d => d.LocalPath!).ToDictionary(g => g.Key, g => g.First().Source);
        if (files.Count == 0)
            return;
        await RunAsync("Extracting metadata", async ct =>
        {
            IReadOnlyList<Document> docs = await AnalysisPipeline.AnalyzeAsync(files, main.CurrentProject!.Id, cancellationToken: ct);
            foreach (Document d in docs)
                d.Source = sources[d.LocalPath!];
            await main.SaveDocumentsAsync(docs);
        });
    }

    private async Task RunAsync(string what, Func<CancellationToken, Task> body)
    {
        work = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0;
        Status = what + "...";
        try
        {
            await body(work.Token);
            Status = what + " finished";
        }
        catch (OperationCanceledException)
        {
            Status = what + " stopped";
            main.Log.Add("MetadataSearch", LogSeverity.Medium, what + " stopped");
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is HttpRequestException)
        {
            Status = what + " failed";
            main.Log.Add("MetadataSearch", LogSeverity.Error, $"{what} failed: {e.Message}");
        }
        finally
        {
            IsBusy = false;
            Progress = 100;
            work.Dispose();
            work = null;
            Refresh();
        }
    }
}
