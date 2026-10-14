using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foca.App.Views;

namespace Foca.App.ViewModels;

public enum LogSeverity
{
    Debug,
    Low,
    Medium,
    High,
    Error,
}

public sealed record LogEntry(DateTime Time, string Source, LogSeverity Severity, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}

/// <summary>The log panel at the bottom of the main window.</summary>
public sealed partial class LogViewModel : ObservableObject
{
    private const int MaxEntries = 5000;
    private readonly IDialogService dialogs;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    [ObservableProperty]
    private bool autoScroll = true;

    public string AutoScrollText => AutoScroll ? "Deactivate AutoScroll" : "Activate AutoScroll";

    public event EventHandler<LogEntry>? EntryAdded;

    public LogViewModel(IDialogService dialogs)
    {
        this.dialogs = dialogs;
    }

    /// <summary>Thread-safe: background work (searches, downloads) logs from worker threads.</summary>
    public void Add(string source, LogSeverity severity, string message)
    {
        var entry = new LogEntry(DateTime.Now, source, severity, message);
        if (Dispatcher.UIThread.CheckAccess())
            Append(entry);
        else
            Dispatcher.UIThread.Post(() => Append(entry));
    }

    private void Append(LogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(0);
        EntryAdded?.Invoke(this, entry);
    }

    partial void OnAutoScrollChanged(bool value) => OnPropertyChanged(nameof(AutoScrollText));

    [RelayCommand]
    private void ToggleAutoScroll() => AutoScroll = !AutoScroll;

    [RelayCommand]
    private void Clear() => Entries.Clear();

    [RelayCommand]
    private async Task SaveAsync()
    {
        string? path = await dialogs.SaveFileAsync("Save log", "foca-log.txt", "txt");
        if (path == null)
            return;
        await File.WriteAllLinesAsync(path, Entries.Select(e => $"{e.Time:yyyy-MM-dd HH:mm:ss}\t{e.Source}\t{e.Severity}\t{e.Message}"));
    }
}
