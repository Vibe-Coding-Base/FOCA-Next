using System.Text.Json;
using System.Text.Json.Serialization;
using Foca.Core;

namespace Foca.App;

public enum AppTheme
{
    /// <summary>Follow the operating system's light/dark setting.</summary>
    System,
    Light,
    Dark,
}

/// <summary>
/// API keys for optional services. Stored encrypted at rest (see <see cref="SecretProtector"/>);
/// the plain values are only held in memory while the application runs.
/// </summary>
public sealed class ApiCredentials
{
    /// <summary>Google Programmable Search Engine API key.</summary>
    public string GoogleApiKey { get; set; } = "";
    /// <summary>Google custom search engine id (cx).</summary>
    public string GoogleCx { get; set; } = "";
    /// <summary>Bing Search API key (the service was retired by Microsoft in August 2025).</summary>
    public string BingApiKey { get; set; } = "";
    /// <summary>Shodan API key.</summary>
    public string ShodanApiKey { get; set; } = "";

    public bool HasGoogleApi => !string.IsNullOrWhiteSpace(GoogleApiKey) && !string.IsNullOrWhiteSpace(GoogleCx);
    public bool HasBingApi => !string.IsNullOrWhiteSpace(BingApiKey);
    public bool HasShodan => !string.IsNullOrWhiteSpace(ShodanApiKey);

    internal ApiCredentials Protect() => new()
    {
        GoogleApiKey = SecretProtector.Protect(GoogleApiKey),
        GoogleCx = GoogleCx,
        BingApiKey = SecretProtector.Protect(BingApiKey),
        ShodanApiKey = SecretProtector.Protect(ShodanApiKey),
    };

    internal ApiCredentials Unprotect() => new()
    {
        GoogleApiKey = SecretProtector.Unprotect(GoogleApiKey),
        GoogleCx = GoogleCx,
        BingApiKey = SecretProtector.Unprotect(BingApiKey),
        ShodanApiKey = SecretProtector.Unprotect(ShodanApiKey),
    };
}

/// <summary>Per-user preferences of the desktop application, stored next to the project database.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>API keys, encrypted on disk.</summary>
    public ApiCredentials Credentials { get; set; } = new();

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
        settings.Credentials = settings.Credentials.Unprotect();
        return settings;
    }

    public void Save()
    {
        string file = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        // Encrypt the in-memory (plain) credentials only for serialisation
        ApiCredentials plain = Credentials;
        Credentials = plain.Protect();
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(this, Options));
        }
        finally
        {
            Credentials = plain;
        }
    }
}
