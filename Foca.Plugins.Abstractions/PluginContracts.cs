using Foca.Core.Analysis;
using Foca.Core.Model;

namespace Foca.Plugins;

/// <summary>Version of this contract. A plugin built for a higher major version is not loaded.</summary>
public static class PluginApi
{
    public const int Version = 2;
}

/// <summary>Entry point of a plugin. The type is named in the plugin's manifest (plugin.json).</summary>
public interface IFocaPlugin
{
    /// <summary>Called once after loading. Register commands, panels and analyzers on <paramref name="host"/>.</summary>
    void Initialize(IPluginHost host);
}

/// <summary>
/// Where a command appears. Mirrors the contextual menus of the original FOCA (API v1), so existing
/// plugins map one to one.
/// </summary>
public enum CommandPlacement
{
    /// <summary>The Plugins menu of the main window.</summary>
    PluginsMenu,
    Project,
    Documents,
    Document,
    MetadataSummary,
    Domains,
    Domain,
    Network,
    NetworkServer,
    NetworkClient,
    IpRange,
}

/// <summary>What a command was invoked on.</summary>
public sealed class CommandContext
{
    public required IProjectContext? Project { get; init; }

    /// <summary>Selected documents (Documents / Document placements).</summary>
    public IReadOnlyList<Document> Documents { get; init; } = Array.Empty<Document>();

    /// <summary>Selected domain, host or IP (Domain / Network placements).</summary>
    public string? Target { get; init; }
}

/// <summary>A menu command contributed by a plugin.</summary>
public sealed class PluginCommand
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public CommandPlacement Placement { get; init; } = CommandPlacement.PluginsMenu;
    public required Func<CommandContext, CancellationToken, Task> Execute { get; init; }

    /// <summary>Optional: hide the command when it does not apply (e.g. requires a domain).</summary>
    public Func<CommandContext, bool>? IsAvailable { get; init; }
}

/// <summary>
/// A panel shown in the main window. <see cref="CreateView"/> returns a view for the host's UI
/// framework (an Avalonia Control in the desktop app); hosts that cannot show it skip the panel.
/// </summary>
public sealed class PluginPanel
{
    public required string Id { get; init; }
    public required string Title { get; init; }

    /// <summary>Show the panel in place of the whole content area instead of a tab.</summary>
    public bool FullWindow { get; init; }

    public required Func<object> CreateView { get; init; }
}

/// <summary>Adds analysis for file types or findings FOCA does not handle itself.</summary>
public interface IDocumentAnalyzerExtension
{
    /// <summary>Extensions handled, with leading dot (".msg").</summary>
    IReadOnlyCollection<string> Extensions { get; }

    /// <summary>Extra findings for a document. Called for every document with a matching extension.</summary>
    Task<IReadOnlyList<Finding>> AnalyzeAsync(string localPath, DocumentAnalysis? builtIn, CancellationToken cancellationToken);
}

/// <summary>Data of the open project, with the operations plugins may perform.</summary>
public interface IProjectContext
{
    Project Project { get; }

    Task<IReadOnlyList<Document>> GetDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds document URLs or local paths; they are downloaded/analysed like any other.</summary>
    Task AddDocumentsAsync(IEnumerable<string> sources, CancellationToken cancellationToken = default);

    /// <summary>Adds domains discovered by the plugin to the project.</summary>
    Task AddDomainsAsync(IEnumerable<string> domains, CancellationToken cancellationToken = default);
}

public enum PluginLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>Services the host offers to plugins.</summary>
public interface IPluginHost
{
    /// <summary>Contract version implemented by the host.</summary>
    int ApiVersion { get; }

    /// <summary>"desktop" or "cli".</summary>
    string HostKind { get; }

    IProjectContext? CurrentProject { get; }

    void Log(PluginLogLevel level, string message);

    void RegisterCommand(PluginCommand command);

    void RegisterPanel(PluginPanel panel);

    void RegisterAnalyzer(IDocumentAnalyzerExtension analyzer);

    event EventHandler<IProjectContext>? ProjectOpened;

    event EventHandler<Document>? DocumentAnalyzed;
}
