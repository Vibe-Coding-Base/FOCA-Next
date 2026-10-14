using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foca.App.Views;
using Foca.Core.Model;

namespace Foca.App.ViewModels;

/// <summary>The project form: create a new project or edit the open one. Mirrors PanelProject of the WinForms app.</summary>
public sealed partial class ProjectPanelViewModel : ObservableObject
{
    private readonly MainWindowViewModel main;
    private readonly IDialogService dialogs;
    private bool nameEdited;

    public ObservableCollection<Project> Projects { get; } = new();

    [ObservableProperty]
    private Project? selectedProject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDomainMode), nameof(DomainLabel))]
    private bool isDocumentMode;

    [ObservableProperty]
    private string name = "Project Name";

    [ObservableProperty]
    private string domain = "";

    [ObservableProperty]
    private string alternativeDomains = "";

    [ObservableProperty]
    private string downloadFolder = "";

    [ObservableProperty]
    private DateTimeOffset? date = DateTimeOffset.Now;

    [ObservableProperty]
    private string notes = "";

    [ObservableProperty]
    private string? error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubmitText))]
    private bool isEditing;

    public bool IsDomainMode
    {
        get => !IsDocumentMode;
        set => IsDocumentMode = !value;
    }

    public string DomainLabel => IsDocumentMode ? "Domain website (add later)" : "Domain website";

    public string SubmitText => IsEditing ? "Update" : "Create";

    public ProjectPanelViewModel(MainWindowViewModel main, IDialogService dialogs)
    {
        this.main = main;
        this.dialogs = dialogs;
    }

    public void LoadNew(IEnumerable<Project> projects)
    {
        Reset(projects);
        IsEditing = false;
        IsDocumentMode = false;
        Name = "Project Name";
        Domain = AlternativeDomains = Notes = "";
        DownloadFolder = "";
        Date = DateTimeOffset.Now;
        nameEdited = false;
    }

    public void LoadExisting(Project project, IEnumerable<Project> projects)
    {
        Reset(projects);
        IsEditing = true;
        Fill(project);
    }

    private void Reset(IEnumerable<Project> projects)
    {
        Projects.Clear();
        foreach (Project p in projects)
            Projects.Add(p);
        SelectedProject = null;
        Error = null;
    }

    private void Fill(Project project)
    {
        IsDocumentMode = !project.HasDomain;
        Name = project.Name;
        Domain = project.Domain ?? "";
        AlternativeDomains = string.Join(Environment.NewLine, project.AlternativeDomains);
        DownloadFolder = project.DownloadFolder;
        Date = new DateTimeOffset(project.CreatedAt.ToLocalTime());
        Notes = project.Notes;
        nameEdited = true;
    }

    // Same convenience as the original form: the name follows the domain until edited by hand
    partial void OnDomainChanged(string value)
    {
        if (!nameEdited && !IsEditing)
            SetProperty(ref name, "Project of " + value, nameof(Name));
    }

    partial void OnNameChanged(string value) => nameEdited = true;

    partial void OnSelectedProjectChanged(Project? value)
    {
        if (value != null)
            _ = main.OpenProjectAsync(value);
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        string? folder = await dialogs.PickFolderAsync("Folder where to save documents");
        if (folder != null)
            DownloadFolder = folder;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        Error = Validate();
        if (Error != null)
            return;

        Project project = IsEditing && main.CurrentProject != null ? main.CurrentProject : new Project();
        bool isNew = !IsEditing;
        project.Name = Name.Trim();
        project.Domain = IsDocumentMode ? null : Domain.Trim().ToLowerInvariant();
        project.AlternativeDomains = IsDocumentMode
            ? new List<string>()
            : AlternativeDomains.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(d => d.ToLowerInvariant()).ToList();
        project.DownloadFolder = string.IsNullOrWhiteSpace(DownloadFolder) ? MainWindowViewModel.DefaultDownloadFolder(project.Name) : DownloadFolder.Trim();
        project.Notes = Notes;
        if (Date.HasValue)
            project.CreatedAt = Date.Value.UtcDateTime;

        try
        {
            await main.SaveProjectAsync(project, isNew);
        }
        catch (InvalidOperationException e)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    private void Cancel() => main.ShowDefaultPanel();

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return "Project name is required.";
        if (!IsDocumentMode && Uri.CheckHostName(Domain.Trim()) != UriHostNameType.Dns)
            return "Enter a valid domain, or choose the document analysis mode.";
        return null;
    }
}
