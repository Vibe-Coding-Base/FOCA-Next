using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foca.App.Views;
using Foca.Core.Model;
using Foca.Core.Network;
using Foca.Core.Web;

namespace Foca.App.ViewModels;

/// <summary>Domains of the project: names from Certificate Transparency and the servers leaked by documents,
/// with a TLS/HTTP view of a selected host.</summary>
public sealed partial class DomainsPanelViewModel : ObservableObject
{
    private readonly MainWindowViewModel main;
    private readonly IDialogService dialogs;

    public ObservableCollection<string> Hosts { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand))]
    private string? selectedHost;

    [ObservableProperty]
    private string details = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindSubdomainsCommand), nameof(InspectCommand))]
    private bool isBusy;

    public DomainsPanelViewModel(MainWindowViewModel main, IDialogService dialogs)
    {
        this.main = main;
        this.dialogs = dialogs;
    }

    public bool HasDomain => main.CurrentProject?.HasDomain == true;

    public string Hint => HasDomain ? "" : Project.DomainRequiredMessage;

    public void Refresh()
    {
        Hosts.Clear();
        Project? p = main.CurrentProject;
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (p?.HasDomain == true)
            names.Add(p.Domain!);
        foreach (string alt in p?.AlternativeDomains ?? new List<string>())
            names.Add(alt);
        foreach (string domain in main.KnownDomains)
            names.Add(domain);
        // Servers named in the documents (RDP targets, links...)
        foreach (Finding f in main.Documents.SelectMany(d => d.Findings).Where(f => f.Kind == FindingKind.Server))
            names.Add(f.NormalizedValue);
        foreach (string n in names)
            Hosts.Add(n);
        OnPropertyChanged(nameof(HasDomain));
        OnPropertyChanged(nameof(Hint));
    }

    private bool CanFind() => !IsBusy && HasDomain;

    [RelayCommand(CanExecute = nameof(CanFind))]
    private async Task FindSubdomainsAsync()
    {
        string domain = main.CurrentProject!.Domain!;
        IsBusy = true;
        try
        {
            using HttpClient http = TargetHttpClient.ForServices(TimeSpan.FromSeconds(90));
            IReadOnlyList<string> names = await new CertificateTransparency(http).GetSubdomainsAsync(domain);
            main.AddKnownDomains(names);
            main.Log.Add("DNS", LogSeverity.Medium, $"Certificate Transparency: {names.Count} name(s) for {domain}");
            Refresh();
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is System.Text.Json.JsonException)
        {
            await dialogs.ShowAsync("Certificate Transparency", "crt.sh could not be queried: " + e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanInspect() => !IsBusy && SelectedHost != null;

    [RelayCommand(CanExecute = nameof(CanInspect))]
    private async Task InspectAsync()
    {
        string host = SelectedHost!;
        IsBusy = true;
        var text = new System.Text.StringBuilder();
        try
        {
            try
            {
                TlsReport tls = await TlsInspector.InspectAsync(host);
                text.AppendLine($"TLS {tls.Protocol}, {tls.CipherSuite}");
                text.AppendLine($"Subject: {tls.Subject}");
                text.AppendLine($"Issuer:  {tls.Issuer}");
                text.AppendLine($"Valid:   {tls.NotBefore:yyyy-MM-dd} - {tls.NotAfter:yyyy-MM-dd}");
                if (tls.SubjectAlternativeNames.Count > 0)
                    text.AppendLine("Names:   " + string.Join(", ", tls.SubjectAlternativeNames));
                foreach (string issue in tls.Issues)
                    text.AppendLine("! " + issue);
                main.AddKnownDomains(tls.SubjectAlternativeNames.Where(n => !n.StartsWith('*') && Uri.CheckHostName(n) == UriHostNameType.Dns));
            }
            catch (Exception e) when (e is System.Net.Sockets.SocketException || e is IOException || e is OperationCanceledException || e is System.Security.Authentication.AuthenticationException)
            {
                text.AppendLine("TLS: " + e.Message);
            }

            text.AppendLine();
            try
            {
                using HttpClient http = TargetHttpClient.ForTargets(TimeSpan.FromSeconds(20));
                HttpFingerprintReport report = await HttpFingerprint.ScanAsync(http, new Uri("https://" + host + "/"));
                text.AppendLine($"HTTP {report.StatusCode}  Server: {report.Server ?? "-"}");
                foreach (string t in report.Technologies)
                    text.AppendLine("  " + t);
                if (report.MissingSecurityHeaders.Count > 0)
                    text.AppendLine("Missing: " + string.Join(", ", report.MissingSecurityHeaders));
                foreach (string issue in report.Issues)
                    text.AppendLine("! " + issue);
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
            {
                text.AppendLine("HTTP: " + e.Message);
            }
        }
        finally
        {
            IsBusy = false;
            Details = text.ToString();
            Refresh();
            SelectedHost = host;
        }
    }
}
