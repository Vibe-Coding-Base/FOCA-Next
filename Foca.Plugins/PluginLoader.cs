using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foca.Plugins.Hosting;

/// <summary>Contents of plugin.json, next to the plugin assemblies in its own folder.</summary>
public sealed class PluginManifest
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("author")] public string? Author { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("apiVersion")] public int ApiVersion { get; set; }
    /// <summary>Assembly file name relative to the plugin folder.</summary>
    [JsonPropertyName("assembly")] public string Assembly { get; set; } = "";
    /// <summary>Full name of the <see cref="IFocaPlugin"/> type.</summary>
    [JsonPropertyName("entryType")] public string EntryType { get; set; } = "";
}

public enum PluginState
{
    Loaded,
    /// <summary>Not approved by the user, or its assembly changed since it was approved.</summary>
    NeedsApproval,
    Incompatible,
    Invalid,
    Failed,
}

public sealed record DiscoveredPlugin(string Folder, PluginManifest? Manifest, string? AssemblyPath, string? Sha256, PluginState State, string? Error);

/// <summary>
/// Finds and loads API v2 plugins. Plugins run with the full rights of FOCA (.NET has no code sandbox),
/// so nothing is loaded until the user approves it, and the approval is bound to the SHA-256 of the
/// assembly: a modified or replaced DLL needs a new approval.
/// </summary>
public sealed class PluginLoader
{
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        // Contract types must come from the host so that plugin and host agree on them
        "Foca.Plugins.Abstractions", "Foca.Core", "MetadataExtractCore", "SearcherCore",
    };

    private readonly string pluginsRoot;
    private readonly PluginApprovals approvals;

    public PluginLoader(string pluginsRoot, PluginApprovals approvals)
    {
        this.pluginsRoot = pluginsRoot;
        this.approvals = approvals;
    }

    public static string DefaultPluginsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FOCA", "plugins");

    public IReadOnlyList<DiscoveredPlugin> Discover()
    {
        if (!Directory.Exists(pluginsRoot))
            return Array.Empty<DiscoveredPlugin>();
        return Directory.GetDirectories(pluginsRoot).OrderBy(d => d, StringComparer.Ordinal).Select(Inspect).ToList();
    }

    private DiscoveredPlugin Inspect(string folder)
    {
        string manifestPath = Path.Combine(folder, "plugin.json");
        if (!File.Exists(manifestPath))
            return new DiscoveredPlugin(folder, null, null, null, PluginState.Invalid, "plugin.json not found");

        PluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath));
        }
        catch (JsonException e)
        {
            return new DiscoveredPlugin(folder, null, null, null, PluginState.Invalid, "Invalid plugin.json: " + e.Message);
        }
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Assembly) || string.IsNullOrWhiteSpace(manifest.EntryType))
            return new DiscoveredPlugin(folder, manifest, null, null, PluginState.Invalid, "plugin.json must define id, assembly and entryType");
        if (manifest.ApiVersion != PluginApi.Version)
            return new DiscoveredPlugin(folder, manifest, null, null, PluginState.Incompatible, $"Built for plugin API {manifest.ApiVersion}, this FOCA provides {PluginApi.Version}");

        // The assembly must live inside the plugin folder ("assembly": "../../evil.dll" is rejected)
        string fullFolder = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        string assemblyPath = Path.GetFullPath(Path.Combine(folder, manifest.Assembly));
        if (!assemblyPath.StartsWith(fullFolder, StringComparison.Ordinal) || !File.Exists(assemblyPath))
            return new DiscoveredPlugin(folder, manifest, null, null, PluginState.Invalid, "Assembly not found inside the plugin folder");

        string sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
        PluginState state = approvals.IsApproved(manifest.Id, sha256) ? PluginState.Loaded : PluginState.NeedsApproval;
        return new DiscoveredPlugin(folder, manifest, assemblyPath, sha256, state, null);
    }

    /// <summary>Loads an approved plugin and returns its entry point.</summary>
    public IFocaPlugin Load(DiscoveredPlugin plugin)
    {
        if (plugin.State != PluginState.Loaded || plugin.AssemblyPath == null || plugin.Manifest == null)
            throw new InvalidOperationException($"Plugin in {plugin.Folder} cannot be loaded: {plugin.State} {plugin.Error}");

        // Re-check: the file may have been swapped between discovery and loading
        string current = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(plugin.AssemblyPath)));
        if (!string.Equals(current, plugin.Sha256, StringComparison.Ordinal) || !approvals.IsApproved(plugin.Manifest.Id, current))
            throw new InvalidOperationException($"Plugin '{plugin.Manifest.Name}' changed after approval and must be approved again.");

        var context = new PluginLoadContext(plugin.AssemblyPath);
        Assembly assembly = context.LoadFromAssemblyPath(plugin.AssemblyPath);
        Type? type = assembly.GetType(plugin.Manifest.EntryType, throwOnError: false);
        if (type == null || !typeof(IFocaPlugin).IsAssignableFrom(type))
            throw new InvalidOperationException($"{plugin.Manifest.EntryType} is not an IFocaPlugin in {plugin.Manifest.Assembly}");
        return (IFocaPlugin)Activator.CreateInstance(type)!;
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver;

        public PluginLoadContext(string assemblyPath) : base(System.IO.Path.GetFileNameWithoutExtension(assemblyPath), isCollectible: true)
        {
            resolver = new AssemblyDependencyResolver(assemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name != null && SharedAssemblies.Contains(assemblyName.Name))
                return null; // use the host's copy
            string? path = resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}

/// <summary>User approvals of plugins, stored as id -> approved assembly SHA-256.</summary>
public sealed class PluginApprovals
{
    private readonly string file;
    private readonly Dictionary<string, string> approved;

    public PluginApprovals(string file)
    {
        this.file = file;
        approved = File.Exists(file)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new()
            : new();
    }

    public static string DefaultFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FOCA", "plugin-approvals.json");

    public bool IsApproved(string id, string sha256) =>
        approved.TryGetValue(id, out string? hash) && string.Equals(hash, sha256, StringComparison.Ordinal);

    public void Approve(string id, string sha256)
    {
        approved[id] = sha256;
        Save();
    }

    public void Revoke(string id)
    {
        if (approved.Remove(id))
            Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(approved, new JsonSerializerOptions { WriteIndented = true }));
    }
}
