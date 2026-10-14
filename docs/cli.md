# `foca-cli` reference

`foca-cli` is the command-line interface to the FOCA Next engine. It shares the project database with
the desktop application.

## Global options

| Option | Description |
|---|---|
| `--db <file>` | SQLite database file. Default: the per-user data folder (`%LOCALAPPDATA%\FOCA\foca.db` on Windows, `~/.local/share/FOCA/foca.db` on Linux, `~/Library/Application Support/FOCA/foca.db` on macOS). |
| `--sqlserver "<connection string>"` | Use a SQL Server database instead of SQLite. |

## `analyze` — analyse local documents

Analyse files and folders directly. No project or domain is required.

```sh
foca-cli analyze <paths...> [options]
```

| Option | Description |
|---|---|
| `-r, --recursive` | Include subfolders. |
| `-p, --project <name>` | Save the results in this project (created if missing). |
| `-f, --format <fmt>` | `table` (default), `json`, `csv`, `html`. |
| `-o, --output <file>` | Write the report to a file instead of the console. |
| `--parallel <n>` | Documents analysed concurrently (default: processor count). |

```sh
foca-cli analyze ./docs -r
foca-cli analyze report.pdf resume.docx -f json -o findings.json
foca-cli analyze ./docs -r --project "Audit 2026"
```

Exit code `2` indicates some documents failed to analyse (others still succeeded).

## `project` — manage projects

```sh
foca-cli project list
foca-cli project create <name> [--domain <d>] [--alt <d1> <d2>] [--folder <dir>] [--notes <text>]
foca-cli project show <name|id>
foca-cli project set-domain <name|id> <domain>
foca-cli project delete <name|id> --yes
```

`create` without `--domain` makes a **document analysis** project; `set-domain` adds a domain later,
turning it into a reconnaissance project.

## `search` — find documents of a domain

```sh
foca-cli search <project> [options]
```

| Option | Description |
|---|---|
| `-e, --engine <engines...>` | `GoogleWeb`, `BingWeb`, `DuckDuckGo`, `GoogleApi`, `BingApi`. Default: `DuckDuckGo BingWeb`. |
| `--wayback` | Also list documents archived by the Internet Archive. |
| `--ext <types...>` | File types to search (default: common document types). |
| `--download` | Download and analyse the documents found. |
| `--concurrency <n>` | Parallel downloads (default: 4). |
| `--api-key <key>` | Key for the `GoogleApi` / `BingApi` engines. |
| `--google-cx <id>` | Google custom search engine id. |

```sh
foca-cli search "ACME" --wayback --download
foca-cli search "ACME" -e GoogleApi --api-key $KEY --google-cx $CX --download
```

The project must have a domain.

## `export` — report of a project

```sh
foca-cli export <project> [-f html|json|csv] [-o <file>]
```

## `import-legacy` — import from the original FOCA

```sh
foca-cli import-legacy --connection "<SQL Server connection string>"
```

Imports every project from an original FOCA database. Documents still on disk are re-analysed with the
current extractors; documents only known by URL are imported as "discovered".
