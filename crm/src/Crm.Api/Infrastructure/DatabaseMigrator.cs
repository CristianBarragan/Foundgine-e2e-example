using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Infrastructure;

/// <summary>
/// Applies EF Core migrations (Infrastructure/Migrations) to the CRM SQL Server database. Runs at startup when
/// "Database:MigrateOnStart" is true. SQL Server creates the database itself if it does not exist yet, so a fresh
/// `./start.sh` needs no manual database setup.
/// </summary>
public sealed class DatabaseMigrator(IDbContextFactory<FoundgineDbContext> factory, ILogger<DatabaseMigrator> log)
{
    public async Task MigrateAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
        log.LogInformation("Database is at migration {Migration}",
            (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault());
    }
}
