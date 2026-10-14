# Changelog

All notable changes to FOCA Next are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/), and the project aims to follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

FOCA Next 1.0 — the first cross-platform release, a fork and rewrite of
[FOCA](https://github.com/ElevenPaths/FOCA) (unmaintained since 2021).

### Added
- **Cross-platform** desktop application (Avalonia) and command-line tool (`foca-cli`) for Windows,
  Linux and macOS, built on .NET 10.
- **SQLite** project storage by default (no SQL Server required); SQL Server optional.
- **Document analysis mode**: projects can analyse local files and folders without a domain; a domain
  can be added later to enable web search.
- **Internet Archive (Wayback)** and **Certificate Transparency (crt.sh)** as document and subdomain
  sources.
- **TLS inspection** (protocol, cipher, certificate, SAN host names) and HTTP security-header checks.
- **HTML / JSON / CSV reports**, in the app and the CLI.
- **Plugin API v2**: UI-neutral, isolated per-plugin loading, SHA-256-bound user approval. Example
  plugin in `samples/HelloPlugin`.
- **Options**: encrypted storage of Google / Bing / Shodan API keys.
- **Light / dark / system theme**, remembered across sessions.
- **Import** from the original FOCA's SQL Server database.

### Changed
- Findings are normalised so spelling variants of a value (e.g. `ACME\jdoe`, `jdoe@acme.com`) are
  grouped into one entry across the project.
- Document types updated: PDFsharp 6, MetadataExtractor 2.8; EXIF of embedded images extracted
  correctly on all platforms.
- Dependencies brought up to date; the build fails on any known-vulnerable package.
- Repository reorganised into `src/`, `tests/`, `samples/`, `build/` and `legacy/`.

### Security
- Document parsers hardened against hostile files: XXE and entity-expansion disabled, zip/gzip bombs
  bounded, structure lengths validated before allocation, cyclic structures stopped, regex timeouts,
  bounded recursion. Covered by tests including mutation fuzzing.
- TLS validation relaxed only for audited hosts; third-party services keep full validation.
- API keys stored encrypted at rest.

### Preserved
- The original Windows application is kept as **FOCA Classic** under `legacy/`, still buildable, for
  features not yet ported (active DNS reconnaissance, backups fuzzer, v1 plugins).
