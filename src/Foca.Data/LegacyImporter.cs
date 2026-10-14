using Foca.Core.Analysis;
using Foca.Core.Model;
using Microsoft.Data.SqlClient;

namespace Foca.Data;

public sealed record LegacyImportResult(string ProjectName, int Documents, int Reanalyzed, int MissingFiles);

/// <summary>
/// Imports projects from the database of the original FOCA (SQL Server, Entity Framework 6 schema).
/// Its metadata is spread over dozens of tables, so documents are imported by URL and local path and
/// the files still on disk are analysed again with the current extractors.
/// </summary>
public sealed class LegacyImporter
{
    private readonly string connectionString;
    private readonly ProjectStore store;

    public LegacyImporter(string connectionString, ProjectStore store)
    {
        this.connectionString = connectionString;
        this.store = store;
    }

    private sealed record LegacyProject(int Id, string Name, string? Domain, string? Folder, DateTime Date, string? Notes);

    private sealed record LegacyFile(string? Url, string? Path, string? Ext, long Size, bool Downloaded);

    public async Task<IReadOnlyList<LegacyImportResult>> ImportAllAsync(IProgress<string>? log = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var projects = new List<LegacyProject>();
        await using (var cmd = new SqlCommand("SELECT Id, ProjectName, Domain, FolderToDownload, ProjectDate, ProjectNotes FROM dbo.Projects", connection))
        await using (SqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                projects.Add(new LegacyProject(r.GetInt32(0), r.IsDBNull(1) ? $"Project {r.GetInt32(0)}" : r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetDateTime(4), r.IsDBNull(5) ? null : r.GetString(5)));
            }
        }

        IReadOnlyList<Project> existing = await store.GetProjectsAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<LegacyImportResult>();
        foreach (LegacyProject legacy in projects)
        {
            string name = UniqueName(legacy.Name, existing.Select(p => p.Name).Concat(results.Select(r => r.ProjectName)));
            log?.Report($"Importing '{legacy.Name}' as '{name}'");
            Project project = await store.CreateProjectAsync(new Project
            {
                Name = name,
                Domain = legacy.Domain,
                DownloadFolder = legacy.Folder ?? "",
                CreatedAt = DateTime.SpecifyKind(legacy.Date, DateTimeKind.Local).ToUniversalTime(),
                Notes = legacy.Notes ?? "",
            }, cancellationToken).ConfigureAwait(false);

            List<LegacyFile> files = await ReadFilesAsync(connection, legacy.Id, cancellationToken).ConfigureAwait(false);
            var documents = new List<Document>();
            var toAnalyze = new List<ScannedFile>();
            int missing = 0;
            foreach (LegacyFile file in files)
            {
                string source = !string.IsNullOrWhiteSpace(file.Url) ? file.Url! : file.Path ?? "";
                if (source.Length == 0 || documents.Any(d => d.Source == source))
                    continue;
                bool onDisk = file.Downloaded && !string.IsNullOrWhiteSpace(file.Path) && File.Exists(file.Path);
                if (file.Downloaded && !onDisk)
                    missing++;
                string? extension = onDisk ? DocumentAnalyzer.ResolveExtension(file.Path!) : file.Ext?.ToLowerInvariant();
                if (onDisk && extension != null)
                {
                    toAnalyze.Add(new ScannedFile(file.Path!, extension, file.Size));
                    continue;
                }
                documents.Add(new Document
                {
                    Source = source,
                    LocalPath = onDisk ? file.Path : null,
                    Extension = extension ?? "",
                    Size = file.Size,
                    Status = DocumentStatus.Discovered,
                });
            }

            IReadOnlyList<Document> analyzed = await AnalysisPipeline.AnalyzeAsync(toAnalyze, project.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            // Keep the web origin of downloaded files as their source
            foreach (Document doc in analyzed)
            {
                LegacyFile origin = files.First(f => f.Path == doc.LocalPath);
                if (!string.IsNullOrWhiteSpace(origin.Url))
                    doc.Source = origin.Url!;
            }
            documents.AddRange(analyzed);
            await store.SaveDocumentsAsync(project.Id, documents, cancellationToken).ConfigureAwait(false);
            results.Add(new LegacyImportResult(name, documents.Count, analyzed.Count, missing));
        }
        return results;
    }

    private static async Task<List<LegacyFile>> ReadFilesAsync(SqlConnection connection, int projectId, CancellationToken cancellationToken)
    {
        var files = new List<LegacyFile>();
        await using var cmd = new SqlCommand("SELECT URL, Path, Ext, Size, Downloaded FROM dbo.FilesItems WHERE IdProject = @id", connection);
        cmd.Parameters.AddWithValue("@id", projectId);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            files.Add(new LegacyFile(r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2), r.GetInt32(3), r.GetBoolean(4)));
        }
        return files;
    }

    private static string UniqueName(string name, IEnumerable<string> taken)
    {
        var set = new HashSet<string>(taken, StringComparer.Ordinal);
        string candidate = name;
        for (int i = 2; set.Contains(candidate); i++)
            candidate = $"{name} ({i})";
        return candidate;
    }
}
