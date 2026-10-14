using Foca.Core.Model;
using Foca.Plugins;

namespace HelloPlugin;

/// <summary>Adds a "Count users" command to the Plugins menu.</summary>
public sealed class Plugin : IFocaPlugin
{
    public void Initialize(IPluginHost host)
    {
        host.RegisterCommand(new PluginCommand
        {
            Id = "example.hello.count-users",
            Title = "Count users",
            Placement = CommandPlacement.PluginsMenu,
            IsAvailable = context => context.Project != null,
            Execute = async (context, ct) =>
            {
                IReadOnlyList<Document> documents = await context.Project!.GetDocumentsAsync(ct);
                int users = documents.SelectMany(d => d.Findings).Where(f => f.Kind == FindingKind.User)
                    .Select(f => f.NormalizedValue).Distinct().Count();
                host.Log(PluginLogLevel.Info, $"{context.Project.Project.Name}: {users} distinct user(s)");
            },
        });
        host.Log(PluginLogLevel.Info, "Hello plugin initialised");
    }
}
