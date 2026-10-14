using Foca.Core.Model;
using Microsoft.EntityFrameworkCore;

namespace Foca.Data;

public enum DatabaseProvider
{
    /// <summary>Single file, nothing to install; the default on every OS.</summary>
    Sqlite,
    /// <summary>Shared server for teams, or users of the legacy FOCA setup.</summary>
    SqlServer,
}

/// <summary>
/// Shared model. Each provider has its own subclass so that it gets its own migrations
/// (Migrations/Sqlite, Migrations/SqlServer); migrations generated for one provider do not fit the other.
/// </summary>
public abstract class FocaDbContext : DbContext
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Finding> Findings => Set<Finding>();

    protected FocaDbContext(DbContextOptions options) : base(options)
    {
    }

    public static Func<FocaDbContext> CreateFactory(DatabaseProvider provider, string connectionString)
    {
        switch (provider)
        {
            case DatabaseProvider.Sqlite:
                DbContextOptions<SqliteFocaDbContext> sqlite = new DbContextOptionsBuilder<SqliteFocaDbContext>()
                    .UseSqlite(connectionString).Options;
                return () => new SqliteFocaDbContext(sqlite);
            case DatabaseProvider.SqlServer:
                DbContextOptions<SqlServerFocaDbContext> sqlServer = new DbContextOptionsBuilder<SqlServerFocaDbContext>()
                    .UseSqlServer(connectionString).Options;
                return () => new SqlServerFocaDbContext(sqlServer);
            default:
                throw new ArgumentOutOfRangeException(nameof(provider));
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(e =>
        {
            e.Property(p => p.Name).IsRequired().HasMaxLength(256);
            e.HasIndex(p => p.Name).IsUnique();
            e.Property(p => p.Domain).HasMaxLength(253);
            e.Property(p => p.DownloadFolder).HasMaxLength(1024);
            e.Ignore(p => p.HasDomain);
            e.Ignore(p => p.Mode);
            e.HasMany<Document>().WithOne().HasForeignKey(d => d.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Document>(e =>
        {
            e.Property(d => d.Source).IsRequired().HasMaxLength(2048);
            e.Property(d => d.LocalPath).HasMaxLength(1024);
            e.Property(d => d.Extension).HasMaxLength(16);
            e.Property(d => d.Sha256).HasMaxLength(64);
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(16);
            e.Ignore(d => d.IsLocal);
            // SQL Server limits index keys to 1700 bytes, so uniqueness of the full URL is enforced by the store
            e.HasIndex(d => d.ProjectId);
            e.HasIndex(d => d.Sha256);
            e.HasMany(d => d.Findings).WithOne().HasForeignKey(f => f.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Finding>(e =>
        {
            e.Property(f => f.Kind).HasConversion<string>().HasMaxLength(32);
            e.Property(f => f.Value).IsRequired().HasMaxLength(2048);
            e.Property(f => f.NormalizedValue).IsRequired().HasMaxLength(2048);
            e.Property(f => f.Context).HasMaxLength(1024);
            e.HasIndex(f => new { f.Kind, f.DocumentId });
        });
    }
}
