using System.CommandLine;
using Foca.Cli;

// FOCA command line: the same analysis engine as the desktop application, on Windows, Linux and macOS.
var dbOption = new Option<string?>("--db") { Description = $"SQLite database file (default: {Foca.Data.ProjectStore.DefaultDatabasePath})", Recursive = true };
var sqlServerOption = new Option<string?>("--sqlserver") { Description = "Use a SQL Server database instead of SQLite (connection string)", Recursive = true };
var root = new RootCommand("FOCA - Fingerprinting Organizations with Collected Archives. Finds metadata and hidden information in documents.");
root.Options.Add(dbOption);
root.Options.Add(sqlServerOption);

StoreFactory stores = new(dbOption, sqlServerOption);
root.Subcommands.Add(AnalyzeCommand.Create(stores));
root.Subcommands.Add(ProjectCommands.Create(stores));
root.Subcommands.Add(SearchCommand.Create(stores));
root.Subcommands.Add(ExportCommand.Create(stores));
root.Subcommands.Add(ImportLegacyCommand.Create(stores));

return await root.Parse(args).InvokeAsync();
