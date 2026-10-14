using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Foca.App.ViewModels;

/// <summary>
/// Application options: API keys for the optional services (Google Programmable Search, Bing, Shodan).
/// Mirrors the Options window of the original FOCA. Keys are stored encrypted (see SecretProtector).
/// </summary>
public sealed partial class OptionsPanelViewModel : ObservableObject
{
    private readonly MainWindowViewModel main;
    private readonly ApiCredentials credentials;

    [ObservableProperty] private string googleApiKey;
    [ObservableProperty] private string googleCx;
    [ObservableProperty] private string bingApiKey;
    [ObservableProperty] private string shodanApiKey;
    [ObservableProperty] private string? status;

    public OptionsPanelViewModel(MainWindowViewModel main, ApiCredentials credentials)
    {
        this.main = main;
        this.credentials = credentials;
        googleApiKey = credentials.GoogleApiKey;
        googleCx = credentials.GoogleCx;
        bingApiKey = credentials.BingApiKey;
        shodanApiKey = credentials.ShodanApiKey;
    }

    [RelayCommand]
    private void Save()
    {
        credentials.GoogleApiKey = GoogleApiKey.Trim();
        credentials.GoogleCx = GoogleCx.Trim();
        credentials.BingApiKey = BingApiKey.Trim();
        credentials.ShodanApiKey = ShodanApiKey.Trim();
        main.SaveSettings();
        main.RefreshSearchEngines();
        Status = "API keys saved.";
    }

    [RelayCommand]
    private void Close() => main.ShowDefaultPanel();
}
