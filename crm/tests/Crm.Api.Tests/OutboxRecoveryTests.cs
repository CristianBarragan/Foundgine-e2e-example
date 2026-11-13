using Crm.Api.Domain;
using Crm.Api.Infrastructure;
using Crm.Api.Integration;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.SqlClient;
using Xunit;

/// <summary>
/// CRM outbox failure and recovery on real SQL Server. Each test class run gets its own throwaway database (created from
/// CRM_TEST_CONNECTION, so that needs CREATE DATABASE rights) so other tests' outbox rows cannot be claimed here.
/// </summary>
public sealed class OutboxRecoveryTests : IClassFixture<CrmFactory>, IAsyncLifetime
{
    private readonly CrmFactory _factory;
    private string? _dbName;
    private string? _adminConnection;
    private IDbContextFactory<FoundgineDbContext>? _contexts;

    public OutboxRecoveryTests(CrmFactory factory) { _factory = factory; }

    public async Task InitializeAsync()
    {
        if (CrmFactory.Connection is not { } cs) return;
        _dbName = $"crm_outbox_{Guid.NewGuid():N}";
        var admin = new SqlConnectionStringBuilder(cs) { InitialCatalog = "master" };
        _adminConnection = admin.ConnectionString;
        await using (var conn = new SqlConnection(_adminConnection))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($"CREATE DATABASE [{_dbName}]", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        var own = new SqlConnectionStringBuilder(cs) { InitialCatalog = _dbName }.ConnectionString;
        var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("CrmConnectionString", own);
            b.UseSetting("Database:MigrateOnStart", "true");
            b.ConfigureServices(s =>
            {
                // No background publisher may race the manual PublishBatchAsync calls below.
                foreach (var d in s.Where(x => x.ServiceType == typeof(IHostedService) &&
                                               x.ImplementationType == typeof(OutboxPublisherService)).ToList()) s.Remove(d);
            });
        });
        _contexts = host.Services.GetRequiredService<IDbContextFactory<FoundgineDbContext>>(); // starts the host and migrates
    }

    public async Task DisposeAsync()
    {
        if (_dbName is null || _adminConnection is null) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(_adminConnection);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"IF DB_ID(N'{_dbName}') IS NOT NULL BEGIN ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_dbName}]; END", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static void RequireDb() => Skip.If(CrmFactory.Connection is null, "CRM_TEST_CONNECTION not set");

    private OutboxPublisher Publisher(IOutboxRouter router) => new(_contexts!, router, NullLogger<OutboxPublisher>.Instance);

    private async Task<Guid> EnqueueAsync()
    {
        var id = Guid.NewGuid();
        await using var db = await _contexts!.CreateDbContextAsync();
        // The CRM uses SQL Server's retrying execution strategy, which only allows transactions run through it.
        await db.InTransactionAsync(() => Outbox.EnqueueAsync(db, id, "acme", "Test.Event", 1, 1, "{}", default), default);
        return id;
    }

    private async Task<OutboxMessage> RowAsync(Guid id)
    {
        await using var db = await _contexts!.CreateDbContextAsync();
        return await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private async Task MakeDueAsync(Guid id)
    {
        await using var db = await _contexts!.CreateDbContextAsync();
        var past = DateTime.UtcNow.AddMinutes(-1);
        await db.Set<OutboxMessage>().Where(m => m.Id == id)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.NextAttemptAt, past));
    }

    private sealed class ScriptedRouter(Func<OutboxMessage, CancellationToken, Task> dispatch) : IOutboxRouter
    {
        public int Calls;
        public Task DispatchAsync(OutboxMessage row, CancellationToken ct) { Interlocked.Increment(ref Calls); return dispatch(row, ct); }
    }

    [SkippableFact]
    public async Task A_failed_dispatch_backs_off_and_a_later_attempt_delivers_it()
    {
        RequireDb();
        var id = await EnqueueAsync();

        var down = new ScriptedRouter((_, _) => throw new InvalidOperationException("temporal down"));
        Assert.Equal(0, await Publisher(down).PublishBatchAsync(10, default));
        var failed = await RowAsync(id);
        Assert.Null(failed.PublishedAt);
        Assert.Equal(1, failed.PublishAttempts);
        Assert.Contains("temporal down", failed.LastError);
        Assert.True(failed.NextAttemptAt > DateTime.UtcNow, "backoff must push the next attempt into the future");

        var up = new ScriptedRouter((_, _) => Task.CompletedTask);
        Assert.Equal(0, await Publisher(up).PublishBatchAsync(10, default)); // still backing off
        Assert.Equal(0, up.Calls);

        await MakeDueAsync(id);
        Assert.Equal(1, await Publisher(up).PublishBatchAsync(10, default));
        var delivered = await RowAsync(id);
        Assert.NotNull(delivered.PublishedAt);
        Assert.Null(delivered.LastError);
        Assert.Equal(0, await Publisher(up).PublishBatchAsync(10, default)); // never re-sent once published
    }

    [SkippableFact]
    public async Task A_publisher_that_dies_mid_batch_releases_its_rows_for_the_next_publisher()
    {
        RequireDb();
        var id = await EnqueueAsync();

        // OperationCanceledException is not swallowed by the publisher, so it models the process going away mid-dispatch.
        var dying = new ScriptedRouter((_, _) => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Publisher(dying).PublishBatchAsync(10, default));
        var afterCrash = await RowAsync(id);
        Assert.Null(afterCrash.PublishedAt);
        Assert.Equal(0, afterCrash.PublishAttempts); // the claim transaction rolled back, nothing was recorded

        var survivor = new ScriptedRouter((_, _) => Task.CompletedTask);
        Assert.Equal(1, await Publisher(survivor).PublishBatchAsync(10, default));
        Assert.NotNull((await RowAsync(id)).PublishedAt);
    }

    [SkippableFact]
    public async Task A_concurrent_publisher_skips_rows_another_publisher_is_dispatching()
    {
        RequireDb();
        var id = await EnqueueAsync();

        var inFlight = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var first = new ScriptedRouter(async (_, _) => { inFlight.SetResult(); await release.Task; });
        var firstRun = Task.Run(() => Publisher(first).PublishBatchAsync(10, default));
        await inFlight.Task; // the first publisher now holds the row lock

        var second = new ScriptedRouter((_, _) => Task.CompletedTask);
        Assert.Equal(0, await Publisher(second).PublishBatchAsync(10, default));
        Assert.Equal(0, second.Calls); // SKIP LOCKED: the second publisher never saw the row

        release.SetResult();
        Assert.Equal(1, await firstRun);
        Assert.Equal(1, first.Calls);
        Assert.NotNull((await RowAsync(id)).PublishedAt);
    }
}
