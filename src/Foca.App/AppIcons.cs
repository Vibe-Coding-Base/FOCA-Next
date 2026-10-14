using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Foca.Core.Model;

namespace Foca.App;

/// <summary>
/// Loads and caches the FOCA tree icons (the original "Arbol lateral" set, bundled as app assets).
/// </summary>
public static class AppIcons
{
    private static readonly Dictionary<string, Bitmap?> Cache = new();

    /// <summary>Returns the bitmap for an icon name (without extension), or null if missing.</summary>
    public static Bitmap? Get(string name)
    {
        if (Cache.TryGetValue(name, out Bitmap? cached))
            return cached;

        Bitmap? bitmap = null;
        try
        {
            var uri = new Uri($"avares://FOCA/Assets/icons/{name}.png");
            if (AssetLoader.Exists(uri))
                bitmap = new Bitmap(AssetLoader.Open(uri));
        }
        catch (Exception)
        {
            bitmap = null;
        }
        Cache[name] = bitmap;
        return bitmap;
    }

    /// <summary>Icon name for a finding kind. Users, e-mails, paths, printers, software, servers and
    /// passwords use the original FOCA category icons; the rest reuse the closest icon of the same set.</summary>
    public static string NameFor(FindingKind kind) => kind switch
    {
        FindingKind.User => "users",
        FindingKind.Email => "emails",
        FindingKind.Path => "folders",
        FindingKind.Printer => "printers",
        FindingKind.Software => "software",
        FindingKind.Server => "servers",
        FindingKind.Password => "passwords",
        FindingKind.OperatingSystem => "clients",
        FindingKind.Company => "domains",
        FindingKind.Template => "documents",
        FindingKind.GeoLocation => "project",
        FindingKind.Link => "network",
        _ => "metadata",
    };

    public static Bitmap? For(FindingKind kind) => Get(NameFor(kind));
}
