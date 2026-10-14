# Contributing to FOCA Next

Thanks for your interest in improving FOCA Next!

## Getting started

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download). On Windows, to also build FOCA
   Classic, install Visual Studio 2022+ or the Build Tools with the **.NET desktop development** workload.
2. Fork and clone the repository.
3. Build and test:

   ```sh
   ./build.ps1
   ```

## Guidelines

- **Discuss big changes first.** Open an issue before starting significant work.
- **Keep the build green.** `./build.ps1` must pass (it runs all tests and, on Windows, FOCA Classic).
- **Add tests** for new behaviour. Document parsers in particular need a test in
  `tests/Foca.Metadata.Tests`; untrusted-input handling needs a hostile-input test.
- **No new vulnerable dependencies.** The build fails on known-vulnerable packages. Add package
  versions in `Directory.Packages.props` (central package management).
- **Match the existing style.** Nullable reference types and file-scoped namespaces in `src/`; follow
  `.editorconfig`. FOCA Classic (`legacy/`) is in maintenance mode — keep changes there minimal.
- **Security matters.** FOCA processes untrusted documents. See [SECURITY.md](SECURITY.md) for the
  invariants the parsers must keep.

## Project layout

See [docs/architecture.md](docs/architecture.md). In short: UI-free engine in `Foca.Core`, data in
`Foca.Data`, front-ends in `Foca.App` (Avalonia) and `Foca.Cli`, parsers in `Foca.Metadata`.

## Commit messages

Write a concise summary line and explain the "why" in the body. Reference issues where relevant.

## License

By contributing, you agree that your contributions are licensed under the [GNU GPL v3](LICENSE).
