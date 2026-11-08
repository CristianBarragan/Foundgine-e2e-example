using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Infrastructure;

/// <summary>
/// Applies EF Core migrations (Infrastructure/Migrations) to the CRM database. Runs at startup when
/// "Database:MigrateOnStart" is true. A database created by the legacy hand-written SQL scripts (it has a
/// schema_migrations table with the last script applied) already contains the initial schema, so those two
/// migrations are recorded in __EFMigrationsHistory (baselined) rather than executed.
/// </summary>
public sealed class DatabaseMigrator(IDbContextFactory<FoundgineDbContext> factory, ILogger<DatabaseMigrator> log)
{
    public const string InitialMigration = "20261113000000_InitialCrm";
    public const string EmailIndexMigration = "20261113000100_CustomerEmailUniqueIndex";
    private const string LastLegacyScript = "006_quote_order_failed.sql";

    public async Task MigrateAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await BaselineLegacyDatabaseAsync(db, ct);
        await db.Database.MigrateAsync(ct);
        log.LogInformation("Database is at migration {Migration}",
            (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault());
    }

    private async Task BaselineLegacyDatabaseAsync(FoundgineDbContext db, CancellationToken ct)
    {
        var legacy = await db.Database
            .SqlQuery<bool>($"SELECT to_regclass('public.schema_migrations') IS NOT NULL AS \"Value\"")
            .SingleAsync(ct);
        if (!legacy) return;

        var complete = await db.Database
            .SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE id = {LastLegacyScript}) AS \"Value\"")
            .SingleAsync(ct);
        if (!complete)
            throw new InvalidOperationException(
                $"Legacy database is not fully migrated ({LastLegacyScript} missing). Apply the legacy scripts first, then restart.");

        if ((await db.Database.GetAppliedMigrationsAsync(ct)).Contains(InitialMigration)) return;

        log.LogWarning("Legacy SQL schema detected; baselining EF migrations without running them.");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId"))
            """, ct);
        // ux_customers_tenant_email already exists in a legacy database, so both migrations are baselined.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ({InitialMigration}, '10.0.0'), ({EmailIndexMigration}, '10.0.0')
            ON CONFLICT DO NOTHING
            """, ct);
    }
}
