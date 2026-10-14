using Foca.Core.Model;
using Microsoft.EntityFrameworkCore;

namespace Foca.Data;

/// <summary>Persistence of projects, documents and findings.</summary>
public sealed class ProjectStore
{
    private readonly Func<FocaDbContext> contextFactory;

    public ProjectStore(Func<FocaDbContext> contextFactory, string location = "")
    {
        this.contextFactory = contextFactory;
        Location = location;
    }

    /// <summary>Database file or server, for display.</summary>
    public string Location { get; }

    /// <summary>The per-user SQLite database: %LOCALAPPDATA%\FOCA on Windows, ~/.local/share/FOCA on Linux, ~/Library/Application Support/FOCA on macOS.</summary>
    public static string DefaultDatabasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "FOCA", "foca.db");

    public static ProjectStore OpenSqlite(string? path = null)
    {
        path = Path.GetFullPath(path ?? DefaultDatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new ProjectStore(FocaDbContext.CreateFactory(DatabaseProvider.Sqlite, $"Data Source={path}"), path);
    }

    public static ProjectStore OpenSqlServer(string connectionString) =>
        new(FocaDbContext.CreateFactory(DatabaseProvider.SqlServer, connectionString), new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString).DataSource);

    private FocaDbContext Context() => contextFactory();

    /// <summary>Creates or upgrades the schema.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using FocaDbContext db = Context();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Project> CreateProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
            throw new ArgumentException("A project name is required.", nameof(project));
        project.Domain = string.IsNullOrWhiteSpace(project.Domain) ? null : project.Domain.Trim().ToLowerInvariant();
        await using FocaDbContext db = Context();
        if (await db.Projects.AnyAsync(p => p.Name == project.Name, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"A project named '{project.Name}' already exists.");
        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return project;
    }

    public async Task UpdateProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        project.Domain = string.IsNullOrWhiteSpace(project.Domain) ? null : project.Domain.Trim().ToLowerInvariant();
        await using FocaDbContext db = Context();
        db.Projects.Update(project);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        await using FocaDbContext db = Context();
        return await db.Projects.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Project?> FindProjectAsync(string nameOrId, CancellationToken cancellationToken = default)
    {
        await using FocaDbContext db = Context();
        if (int.TryParse(nameOrId, out int id))
        {
            Project? byId = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
            if (byId != null)
                return byId;
        }
        return await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Name == nameOrId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteProjectAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await using FocaDbContext db = Context();
        await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Document>> GetDocumentsAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await using FocaDbContext db = Context();
        return await db.Documents.AsNoTracking()
            .Include(d => d.Findings)
            .Where(d => d.ProjectId == projectId)
            .OrderBy(d => d.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts or replaces documents of a project, matched by <see cref="Document.Source"/>; findings of a
    /// replaced document are replaced as a whole.
    /// </summary>
    public async Task SaveDocumentsAsync(int projectId, IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        await using FocaDbContext db = Context();
        // Old findings are deleted and new rows inserted in separate commands: keep them atomic
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        List<Document> incoming = documents.ToList();
        HashSet<string> sources = incoming.Select(d => d.Source).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, Document> existing = await db.Documents
            .Where(d => d.ProjectId == projectId && sources.Contains(d.Source))
            .ToDictionaryAsync(d => d.Source, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);

        foreach (Document doc in incoming)
        {
            doc.ProjectId = projectId;
            if (existing.TryGetValue(doc.Source, out Document? old))
            {
                await db.Findings.Where(f => f.DocumentId == old.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                db.Documents.Remove(old);
            }
            doc.Id = 0;
            foreach (Finding f in doc.Findings)
                f.Id = 0;
            db.Documents.Add(doc);
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasDocumentAsync(int projectId, string source, CancellationToken cancellationToken = default)
    {
        await using FocaDbContext db = Context();
        return await db.Documents.AnyAsync(d => d.ProjectId == projectId && d.Source == source, cancellationToken).ConfigureAwait(false);
    }
}
