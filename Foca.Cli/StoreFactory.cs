using System.CommandLine;
using Foca.Core.Model;
using Foca.Data;

namespace Foca.Cli;

/// <summary>Opens the project database selected by the global --db / --sqlserver options.</summary>
internal sealed class StoreFactory
{
    private readonly Option<string?> db;
    private readonly Option<string?> sqlServer;

    public StoreFactory(Option<string?> db, Option<string?> sqlServer)
    {
        this.db = db;
        this.sqlServer = sqlServer;
    }

    public async Task<ProjectStore> OpenAsync(ParseResult parse, CancellationToken cancellationToken)
    {
        string? connection = parse.GetValue(sqlServer);
        ProjectStore store = string.IsNullOrWhiteSpace(connection) ? ProjectStore.OpenSqlite(parse.GetValue(db)) : ProjectStore.OpenSqlServer(connection);
        await store.InitializeAsync(cancellationToken);
        return store;
    }

    public async Task<(ProjectStore Store, Project Project)> OpenProjectAsync(ParseResult parse, string nameOrId, CancellationToken cancellationToken)
    {
        ProjectStore store = await OpenAsync(parse, cancellationToken);
        Project project = await store.FindProjectAsync(nameOrId, cancellationToken)
            ?? throw new CliException($"Project '{nameOrId}' not found. List projects with: foca project list");
        return (store, project);
    }
}

internal sealed class CliException : Exception
{
    public CliException(string message) : base(message)
    {
    }
}
