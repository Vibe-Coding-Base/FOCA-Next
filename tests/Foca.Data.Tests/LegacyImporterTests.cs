using System.Runtime.CompilerServices;
using Foca.Core.Model;
using Foca.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Foca.Data.Tests;

/// <summary>Imports from a database shaped like the original FOCA's (EF6 tables dbo.Projects and dbo.FilesItems).</summary>
public sealed class LegacyImporterTests : IDisposable
{
    private readonly string database = "FocaLegacy_" + Guid.NewGuid().ToString("N");
    private readonly string sqlite = Path.Combine(Path.GetTempPath(), "foca-" + Guid.NewGuid().ToString("N") + ".db");
    private const string Server = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connect Timeout=60";

    private static string Corpus([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "corpus"));

    private static bool LocalDbAvailable()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var c = new SqlConnection(Server);
            c.Open();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    private void Exec(string connection, string sql)
    {
        using var c = new SqlConnection(connection);
        c.Open();
        using var cmd = new SqlCommand(sql, c);
        cmd.ExecuteNonQuery();
    }

    [SkippableFact]
    public async Task ImportsProjectsAndReanalysesFilesOnDisk()
    {
        Skip.IfNot(LocalDbAvailable(), "SQL Server LocalDB is not available");
        Exec(Server, $"CREATE DATABASE [{database}]");
        string legacy = Server + $";Database={database}";
        Exec(legacy, """
            CREATE TABLE dbo.Projects (Id int IDENTITY PRIMARY KEY, ProjectName nvarchar(max), ProjectSaveFile nvarchar(max), ProjectState int NOT NULL,
                Domain nvarchar(max), FolderToDownload nvarchar(max), ProjectDate datetime NOT NULL, ProjectNotes nvarchar(max));
            CREATE TABLE dbo.FilesItems (Id int IDENTITY PRIMARY KEY, IdProject int NOT NULL, Ext nvarchar(max), ModifiedDate datetime NOT NULL,
                URL nvarchar(max), Path nvarchar(max), Date datetime NOT NULL, Size int NOT NULL, Downloaded bit NOT NULL, Processed bit NOT NULL, Metadata_Id int NULL);
            """);
        string pdf = Path.Combine(Corpus(), "sample.pdf");
        Exec(legacy, $"""
            INSERT dbo.Projects (ProjectName, ProjectState, Domain, FolderToDownload, ProjectDate, ProjectNotes) VALUES (N'Old project', 2, N'acme.com', N'C:\tmp', '2019-05-01', N'notes');
            INSERT dbo.FilesItems (IdProject, Ext, ModifiedDate, URL, Path, Date, Size, Downloaded, Processed) VALUES
                (1, '.pdf', '2019-05-01', N'https://acme.com/memo.pdf', N'{pdf}', '2019-05-01', 100, 1, 1),
                (1, '.docx', '2019-05-01', N'https://acme.com/gone.docx', N'C:\does\not\exist.docx', '2019-05-01', 100, 1, 1),
                (1, '.xls', '2019-05-01', N'https://acme.com/later.xls', NULL, '2019-05-01', 0, 0, 0);
            """);

        ProjectStore store = ProjectStore.OpenSqlite(sqlite);
        await store.InitializeAsync();
        IReadOnlyList<LegacyImportResult> results = await new LegacyImporter(legacy, store).ImportAllAsync();

        LegacyImportResult result = Assert.Single(results);
        Assert.Equal(3, result.Documents);
        Assert.Equal(1, result.Reanalyzed);
        Assert.Equal(1, result.MissingFiles);

        Project project = (await store.FindProjectAsync("Old project"))!;
        Assert.Equal("acme.com", project.Domain);
        IReadOnlyList<Document> docs = await store.GetDocumentsAsync(project.Id);
        Document memo = docs.Single(d => d.Source == "https://acme.com/memo.pdf");
        Assert.Equal(DocumentStatus.Analyzed, memo.Status);
        Assert.Contains(memo.Findings, f => f.Kind == FindingKind.User && f.Value == "henry.pdfauthor");
        Assert.Equal(DocumentStatus.Discovered, docs.Single(d => d.Source.EndsWith("later.xls")).Status);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(sqlite);
        if (!LocalDbAvailable())
            return;
        SqlConnection.ClearAllPools();
        Exec(Server, $"IF DB_ID('{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}] END");
    }
}
