using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foca.App;

public enum AppTheme
{
    /// <summary>Follow the operating system's light/dark setting.</summary>
    System,
    Light,
    Dark,
}

/// <summary>Per-user preferences of the desktop application, stored next to the project database.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>File the settings were loaded from and are saved to.</summary>
    [JsonIgnore]
    public string FilePath { get; private set; } = DefaultFile;

    public static string DefaultFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FOCA", "settings.json");

    /// <summary>Missing or unreadable settings fall back to defaults instead of preventing start-up.</summary>
    public static AppSettings Load(string? file = null)
    {
        file ??= DefaultFile;
        AppSettings settings;
        try
        {
            settings = File.Exists(file) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), Options) ?? new() : new();
        }
        catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
        {
            settings = new();
        }
        settings.FilePath = file;
        return settings;
    }

    public void Save()
    {
        string file = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this, Options));
    }
}
