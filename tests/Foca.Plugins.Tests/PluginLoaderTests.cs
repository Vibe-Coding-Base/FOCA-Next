using System.Runtime.CompilerServices;
using Foca.Core.Model;
using Foca.Plugins;
using Foca.Plugins.Hosting;
using Xunit;

namespace Foca.Plugins.Tests;

public sealed class PluginLoaderTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("foca-plugins").FullName;
    private readonly string pluginsDir;
    private readonly PluginApprovals approvals;

    public PluginLoaderTests()
    {
        pluginsDir = Path.Combine(root, "plugins");
        approvals = new PluginApprovals(Path.Combine(root, "approvals.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string HelloOutput([CallerFilePath] string source = "")
    {
        string config = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name; // bin/<Configuration>/net10.0
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "samples", "HelloPlugin", "bin", config, "net10.0"));
    }

    private string Install()
    {
        string target = Path.Combine(pluginsDir, "hello");
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(HelloOutput()))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        return target;
    }

    [Fact]
    public void UnapprovedPluginIsNotLoaded()
    {
        Install();
        var loader = new PluginLoader(pluginsDir, approvals);
        DiscoveredPlugin plugin = Assert.Single(loader.Discover());
        Assert.Equal(PluginState.NeedsApproval, plugin.State);
        Assert.Throws<InvalidOperationException>(() => loader.Load(plugin));
    }

    [Fact]
    public async Task ApprovedPluginLoadsAndRegistersItsCommand()
    {
        Install();
        var loader = new PluginLoader(pluginsDir, approvals);
        DiscoveredPlugin found = Assert.Single(loader.Discover());
        foreach (string shared in new[] { "Foca.Plugins.Abstractions.dll", "Foca.Core.dll", "Foca.Metadata.dll" })
            Assert.False(File.Exists(Path.Combine(found.Folder, shared)), shared + " must come from the host");
        approvals.Approve(found.Manifest!.Id, found.Sha256!);

        DiscoveredPlugin plugin = Assert.Single(loader.Discover());
        Assert.Equal(PluginState.Loaded, plugin.State);

        var logs = new List<string>();
        var registry = new PluginRegistry("test", (_, m) => logs.Add(m));
        Assert.True(registry.Initialize(loader.Load(plugin), plugin.Manifest!.Name));
        Assert.Contains("Hello plugin initialised", logs);

        var project = new FakeProject();
        var context = new CommandContext { Project = project };
        PluginCommand command = Assert.Single(registry.CommandsFor(CommandPlacement.PluginsMenu, context));
        await command.Execute(context, CancellationToken.None);
        Assert.Contains("p: 2 distinct user(s)", logs);

        Assert.Empty(registry.CommandsFor(CommandPlacement.PluginsMenu, new CommandContext { Project = null }));
    }

    [Fact]
    public void ModifiedAssemblyNeedsNewApproval()
    {
        string folder = Install();
        var loader = new PluginLoader(pluginsDir, approvals);
        DiscoveredPlugin found = Assert.Single(loader.Discover());
        approvals.Approve(found.Manifest!.Id, found.Sha256!);

        File.AppendAllText(Path.Combine(folder, "HelloPlugin.dll"), "tampered");
        Assert.Equal(PluginState.NeedsApproval, Assert.Single(loader.Discover()).State);
    }

    [Fact]
    public void ManifestCannotPointOutsideThePluginFolder()
    {
        string folder = Install();
        File.WriteAllText(Path.Combine(folder, "plugin.json"),
            """{"id":"x","name":"x","version":"1","apiVersion":2,"assembly":"../../outside.dll","entryType":"X"}""");
        File.WriteAllText(Path.Combine(root, "outside.dll"), "");
        DiscoveredPlugin plugin = Assert.Single(new PluginLoader(pluginsDir, approvals).Discover());
        Assert.Equal(PluginState.Invalid, plugin.State);
    }

    [Fact]
    public void PluginForAnotherApiVersionIsIncompatible()
    {
        string folder = Install();
        string json = File.ReadAllText(Path.Combine(folder, "plugin.json")).Replace("\"apiVersion\": 2", "\"apiVersion\": 3");
        File.WriteAllText(Path.Combine(folder, "plugin.json"), json);
        Assert.Equal(PluginState.Incompatible, Assert.Single(new PluginLoader(pluginsDir, approvals).Discover()).State);
    }

    [Fact]
    public void FaultyEventHandlerDoesNotBreakTheHost()
    {
        var logs = new List<string>();
        var registry = new PluginRegistry("test", (_, m) => logs.Add(m));
        registry.ProjectOpened += (_, _) => throw new InvalidOperationException("boom");
        registry.OpenProject(new FakeProject());
        Assert.Contains(logs, l => l.Contains("boom"));
    }

    private sealed class FakeProject : IProjectContext
    {
        public Project Project { get; } = new() { Name = "p" };

        public Task<IReadOnlyList<Document>> GetDocumentsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Document>>(new[]
            {
                new Document { Findings = { new Finding { Kind = FindingKind.User, NormalizedValue = "a" }, new Finding { Kind = FindingKind.User, NormalizedValue = "b" } } },
                new Document { Findings = { new Finding { Kind = FindingKind.User, NormalizedValue = "a" }, new Finding { Kind = FindingKind.Email, NormalizedValue = "c" } } },
            });

        public Task AddDocumentsAsync(IEnumerable<string> sources, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddDomainsAsync(IEnumerable<string> domains, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
