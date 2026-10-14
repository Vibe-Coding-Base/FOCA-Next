# Architecture and project layout

FOCA Next is split into a UI-free core, two front-ends (desktop and CLI), a data layer and a plugin
system. The original Windows application is kept verbatim under `legacy/`.

```
foca-next/
├── Foca.slnx                     Cross-platform solution
├── Directory.Build.props         Shared metadata, analyzers, vulnerable-package gate
├── Directory.Packages.props      Central package versions
├── build.ps1                     Build + test everything
├── build/publish.ps1             Self-contained packaging
├── src/
│   ├── Foca.Metadata/            Document parsers (Office, PDF, images, SVG, ...). net48 + net10.0
│   ├── Foca.Search/              Search-engine clients. net48 + net10.0
│   ├── Foca.Core/                Analysis pipeline, search providers, downloads, network, reports
│   ├── Foca.Data/                EF Core storage (SQLite / SQL Server), legacy import
│   ├── Foca.Plugins.Abstractions/ Plugin contract (API v2)
│   ├── Foca.Plugins/             Plugin discovery, approval, isolated loading
│   ├── Foca.App/                 Avalonia desktop application
│   └── Foca.Cli/                 foca-cli
├── samples/HelloPlugin/          Example API v2 plugin
├── tests/                        xUnit tests + shared corpus
└── legacy/                       FOCA Classic (WinForms), its PluginsAPI, v1 sample, tests
```

## Dependencies between projects

```
Foca.Metadata ─┐
Foca.Search  ──┴─► Foca.Core ─► Foca.Data ─► Foca.Cli
                        │           │
                        │           └──────► Foca.App ◄─ Foca.Plugins ◄─ Foca.Plugins.Abstractions
                        └────────────────────────────────────────────────┘
```

`Foca.Core` has no UI dependency, so the desktop app and the CLI share exactly the same engine.
`Foca.Metadata` and `Foca.Search` multi-target `net48` so that FOCA Classic can reuse them.

## Key design points

- **Document analysis is domain-optional.** `Project.Domain` may be null; code checks
  `Project.HasDomain` and never treats an empty domain as "match everything".
- **Everything is async and cancellable.** `AnalysisPipeline` uses `Parallel.ForEachAsync` with a
  per-document timeout instead of the original thread-per-task model (`Thread.Abort` does not exist
  on modern .NET).
- **Findings are normalised.** Spelling variants of a value are grouped by a canonical key
  (`FindingNormalizer`), so one user is one entry across the project.
- **Reports are safe by construction.** HTML is encoded with a restrictive CSP; CSV cells are guarded
  against spreadsheet formula injection.
- **Plugins are isolated.** Each loads in its own `AssemblyLoadContext`, sharing only the contract
  assemblies with the host, and only after SHA-256-bound user approval.

## Build system

Two paths (see `build.ps1`):

1. **FOCA Next** — `dotnet build Foca.slnx`. Works on every OS.
2. **FOCA Classic** — Visual Studio MSBuild on `legacy/FOCA.Classic.slnx`, Windows only.

Shared libraries are referenced by FOCA Classic with `SetTargetFramework=net48` so only their .NET
Framework build is pulled in.
