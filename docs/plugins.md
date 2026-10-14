# Writing plugins (API v2)

FOCA Next plugins are .NET assemblies that add commands, panels and document analyzers. The API is
UI-framework neutral, so a plugin works in the desktop app and (for non-UI parts) the CLI.

> Plugins run with the **same rights as FOCA**. .NET has no code sandbox, so the user must approve
> each plugin before it loads, and the approval is bound to the assembly's SHA-256 — a modified
> plugin must be approved again.

## Project setup

Reference `Foca.Plugins.Abstractions` but do **not** ship it: FOCA provides the contract assemblies
at run time.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Foca.Plugins.Abstractions" Version="2.0.0" />
  </ItemGroup>
</Project>
```

## Manifest

Every plugin folder contains a `plugin.json`:

```json
{
  "id": "example.hello",
  "name": "Hello plugin",
  "version": "1.0.0",
  "author": "You",
  "description": "Counts the users found in the project.",
  "apiVersion": 2,
  "assembly": "HelloPlugin.dll",
  "entryType": "HelloPlugin.Plugin"
}
```

`assembly` must be inside the plugin folder; paths that escape it are rejected.

## Entry point

```csharp
using Foca.Core.Model;
using Foca.Plugins;

public sealed class Plugin : IFocaPlugin
{
    public void Initialize(IPluginHost host)
    {
        host.RegisterCommand(new PluginCommand
        {
            Id = "example.hello.count-users",
            Title = "Count users",
            Placement = CommandPlacement.PluginsMenu,
            IsAvailable = ctx => ctx.Project != null,
            Execute = async (ctx, ct) =>
            {
                var docs = await ctx.Project!.GetDocumentsAsync(ct);
                int users = docs.SelectMany(d => d.Findings)
                                .Where(f => f.Kind == FindingKind.User)
                                .Select(f => f.NormalizedValue).Distinct().Count();
                host.Log(PluginLogLevel.Info, $"{users} distinct user(s)");
            },
        });
    }
}
```

## What a plugin can contribute

- **Commands** (`IPluginHost.RegisterCommand`) placed in the Plugins menu or on context menus
  (`CommandPlacement`), mirroring the original FOCA's menu locations.
- **Panels** (`RegisterPanel`) shown in the main window; the host creates the view for its UI framework.
- **Document analyzers** (`RegisterAnalyzer`) that add findings for file types FOCA does not handle.
- React to **events**: `ProjectOpened`, `DocumentAnalyzed`.

See [`samples/HelloPlugin`](../samples/HelloPlugin) for a complete, buildable example.

## Installing

Build the plugin and copy its output folder (the DLL, its dependencies and `plugin.json`) into the
FOCA plugins folder:

- Windows: `%LOCALAPPDATA%\FOCA\plugins\<name>`
- Linux: `~/.local/share/FOCA/plugins/<name>`
- macOS: `~/Library/Application Support/FOCA/plugins/<name>`

Then open **Plugins → Load / approve plugins** and approve it.
