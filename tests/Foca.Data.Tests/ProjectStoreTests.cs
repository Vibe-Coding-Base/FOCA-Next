using Foca.Core.Model;
using Foca.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Foca.Data.Tests;

public abstract class ProjectStoreContract
{
    protected abstract Task<ProjectStore> OpenAsync();

    private static Document Doc(string source, params string[] users) => new()
    {
        Source = source,
        Extension = ".pdf",
        Status = DocumentStatus.Analyzed,
        Findings = users.Select(u => new Finding { Kind = FindingKind.User, Value = u, NormalizedValue = u.ToLowerInvariant() }).ToList(),
    };

    [SkippableFact]
    public async Task DocumentAnalysisProjectCanGetADomainLater()
    {
        ProjectStore store = await OpenAsync();
        Project p = await store.CreateProjectAsync(new Project { Name = "docs-" + Guid.NewGuid().ToString("N") });
        Assert.False(p.HasDomain);

        p.Domain = "  ACME.com ";
        p.AlternativeDomains = new() { "acme.org" };
        await store.UpdateProjectAsync(p);

        Project reloaded = (await store.FindProjectAsync(p.Name))!;
        Assert.Equal("acme.com", reloaded.Domain);
        Assert.Equal(new[] { "acme.org" }, reloaded.AlternativeDomains);
        Assert.Equal(ProjectMode.DomainReconnaissance, reloaded.Mode);
    }

    [SkippableFact]
    public async Task SavingADocumentAgainReplacesItsFindings()
    {
        ProjectStore store = await OpenAsync();
        Project p = await store.CreateProjectAsync(new Project { Name = "p-" + Guid.NewGuid().ToString("N") });

        await store.SaveDocumentsAsync(p.Id, new[] { Doc("https://acme.com/a.pdf", "alice", "bob"), Doc("https://acme.com/b.pdf", "carol") });
        await store.SaveDocumentsAsync(p.Id, new[] { Doc("https://acme.com/a.pdf", "dave") });

        IReadOnlyList<Document> docs = await store.GetDocumentsAsync(p.Id);
        Assert.Equal(2, docs.Count);
        Assert.Equal(new[] { "dave" }, docs.Single(d => d.Source.EndsWith("a.pdf")).Findings.Select(f => f.Value));
        Assert.Equal(new[] { "carol" }, docs.Single(d => d.Source.EndsWith("b.pdf")).Findings.Select(f => f.Value));
    }

    [SkippableFact]
    public async Task DeletingAProjectRemovesItsResults()
    {
        ProjectStore store = await OpenAsync();
        Project p = await store.CreateProjectAsync(new Project { Name = "d-" + Guid.NewGuid().ToString("N") });
        await store.SaveDocumentsAsync(p.Id, new[] { Doc("x.pdf", "alice") });
        await store.DeleteProjectAsync(p.Id);
        Assert.Null(await store.FindProjectAsync(p.Name));
        Assert.Empty(await store.GetDocumentsAsync(p.Id));
    }

    [SkippableFact]
    public async Task ProjectNamesAreUnique()
    {
        ProjectStore store = await OpenAsync();
        string name = "u-" + Guid.NewGuid().ToString("N");
        await store.CreateProjectAsync(new Project { Name = name });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateProjectAsync(new Project { Name = name }));
    }
}

public sealed class SqliteProjectStoreTests : ProjectStoreContract, IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "foca-" + Guid.NewGuid().ToString("N") + ".db");

    protected override async Task<ProjectStore> OpenAsync()
    {
        ProjectStore store = ProjectStore.OpenSqlite(path);
        await store.InitializeAsync();
        return store;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
    }
}

/// <summary>Runs against SQL Server LocalDB when it is installed (Windows developer machines).</summary>
public sealed class SqlServerProjectStoreTests : ProjectStoreContract, IDisposable
{
    private readonly string database = "FocaTest_" + Guid.NewGuid().ToString("N");
    private string Connection => $@"Server=(localdb)\MSSQLLocalDB;Database={database};Integrated Security=true;Connect Timeout=60";

    private static bool LocalDbAvailable()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var c = new SqlConnection(@"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connect Timeout=60");
            c.Open();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    protected override async Task<ProjectStore> OpenAsync()
    {
        Skip.IfNot(LocalDbAvailable(), "SQL Server LocalDB is not available");
        ProjectStore store = ProjectStore.OpenSqlServer(Connection);
        await store.InitializeAsync();
        return store;
    }

    public void Dispose()
    {
        if (!LocalDbAvailable())
            return;
        SqlConnection.ClearAllPools();
        using var c = new SqlConnection(@"Server=(localdb)\MSSQLLocalDB;Integrated Security=true");
        c.Open();
        using var cmd = new SqlCommand($"IF DB_ID('{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}] END", c);
        cmd.ExecuteNonQuery();
    }
}
