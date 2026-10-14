using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Foca.App.ViewModels;
using Foca.App.Views;
using Foca.Data;

namespace Foca.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public void ApplyTheme(AppTheme theme) => RequestedThemeVariant = theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppSettings settings = AppSettings.Load();
            ApplyTheme(settings.Theme);

            var window = new MainWindow();
            var dialogs = new DialogService(window);
            // --db <file> opens another SQLite database (same option as the CLI)
            string[] args = desktop.Args ?? Array.Empty<string>();
            int db = Array.IndexOf(args, "--db");
            ProjectStore store = ProjectStore.OpenSqlite(db >= 0 && db + 1 < args.Length ? args[db + 1] : null);
            var vm = new MainWindowViewModel(store, dialogs, settings, ApplyTheme);
            window.DataContext = vm;
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await vm.InitializeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
