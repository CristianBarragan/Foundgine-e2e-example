using System.Reflection;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Infrastructure;

/// <summary>
/// Applies the embedded db/migrations/*.sql files in name order, once each, under an advisory lock. Tracked in
/// schema_migrations. Runs at startup when "Database:MigrateOnStart" is true.
/// </summary>
public sealed class DatabaseMigrator(IDbContextFactory<FoundgineDbContext> factory, ILogger<DatabaseMigrator> log)
{
    private const long LockKey = 7_301_001;

    public async Task MigrateAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({LockKey})", ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS schema_migrations (id text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())", ct);

        var asm = Assembly.GetExecutingAssembly();
        foreach (var resource in asm.GetManifestResourceNames().Where(n => n.EndsWith(".sql")).OrderBy(n => n, StringComparer.Ordinal))
        {
            var id = resource[(resource.LastIndexOf("migrations.", StringComparison.Ordinal) + "migrations.".Length)..];
            var applied = await db.Database
                .SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM schema_migrations WHERE id = {0}", id)
                .AnyAsync(ct);
            if (applied) continue;

            using var reader = new StreamReader(asm.GetManifestResourceStream(resource)!);
            var sql = await reader.ReadToEndAsync(ct);
            await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"), ct);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO schema_migrations (id) VALUES ({0})", [id], ct);
            log.LogInformation("Applied migration {Id}", id);
        }

        await tx.CommitAsync(ct);
    }
}
