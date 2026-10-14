using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Foca.Data;

public sealed class SqliteFocaDbContext : FocaDbContext
{
    public SqliteFocaDbContext(DbContextOptions<SqliteFocaDbContext> options) : base(options)
    {
    }
}

public sealed class SqlServerFocaDbContext : FocaDbContext
{
    public SqlServerFocaDbContext(DbContextOptions<SqlServerFocaDbContext> options) : base(options)
    {
    }
}

/// <summary>Used by "dotnet ef migrations add ... --context SqliteFocaDbContext".</summary>
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteFocaDbContext>
{
    public SqliteFocaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteFocaDbContext>().UseSqlite("Data Source=design.db").Options);
}

/// <summary>Used by "dotnet ef migrations add ... --context SqlServerFocaDbContext".</summary>
public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerFocaDbContext>
{
    public SqlServerFocaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqlServerFocaDbContext>().UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=FocaDesign;Trusted_Connection=True").Options);
}
