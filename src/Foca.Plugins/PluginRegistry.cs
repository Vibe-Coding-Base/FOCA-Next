using Foca.Core.Model;

namespace Foca.Plugins.Hosting;

/// <summary>
/// Base <see cref="IPluginHost"/> that records what plugins contribute. Hosts (desktop app, CLI) derive
/// from it to provide the project context and show commands and panels.
/// </summary>
public class PluginRegistry : IPluginHost
{
    private readonly List<PluginCommand> commands = new();
    private readonly List<PluginPanel> panels = new();
    private readonly List<IDocumentAnalyzerExtension> analyzers = new();
    private readonly Action<PluginLogLevel, string> log;

    public PluginRegistry(string hostKind, Action<PluginLogLevel, string> log)
    {
        HostKind = hostKind;
        this.log = log;
    }

    public int ApiVersion => PluginApi.Version;

    public string HostKind { get; }

    public IProjectContext? CurrentProject { get; private set; }

    public IReadOnlyList<PluginCommand> Commands => commands;

    public IReadOnlyList<PluginPanel> Panels => panels;

    public IReadOnlyList<IDocumentAnalyzerExtension> Analyzers => analyzers;

    public event EventHandler<IProjectContext>? ProjectOpened;

    public event EventHandler<Document>? DocumentAnalyzed;

    public void Log(PluginLogLevel level, string message) => log(level, message);

    public void RegisterCommand(PluginCommand command)
    {
        if (commands.Any(c => c.Id == command.Id))
            throw new InvalidOperationException($"A command with id '{command.Id}' is already registered.");
        commands.Add(command);
    }

    public void RegisterPanel(PluginPanel panel) => panels.Add(panel);

    public void RegisterAnalyzer(IDocumentAnalyzerExtension analyzer) => analyzers.Add(analyzer);

    public IEnumerable<PluginCommand> CommandsFor(CommandPlacement placement, CommandContext context) =>
        commands.Where(c => c.Placement == placement && (c.IsAvailable?.Invoke(context) ?? true));

    public void OpenProject(IProjectContext project)
    {
        CurrentProject = project;
        RaiseSafely(() => ProjectOpened?.Invoke(this, project), nameof(ProjectOpened));
    }

    public void NotifyDocumentAnalyzed(Document document) =>
        RaiseSafely(() => DocumentAnalyzed?.Invoke(this, document), nameof(DocumentAnalyzed));

    /// <summary>Initialises a plugin, isolating the host from its exceptions.</summary>
    public bool Initialize(IFocaPlugin plugin, string name)
    {
        try
        {
            plugin.Initialize(this);
            return true;
        }
        catch (Exception e)
        {
            log(PluginLogLevel.Error, $"Plugin '{name}' failed to initialise: {e.Message}");
            return false;
        }
    }

    // A faulty plugin handler must not break the host's workflow
    private void RaiseSafely(Action raise, string eventName)
    {
        try
        {
            raise();
        }
        catch (Exception e)
        {
            log(PluginLogLevel.Error, $"A plugin failed handling {eventName}: {e.Message}");
        }
    }
}
