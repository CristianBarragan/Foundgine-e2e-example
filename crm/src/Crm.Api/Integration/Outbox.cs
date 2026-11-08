using System.Text.Json;
using Crm.Api.Domain;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Integration;

public static class Outbox
{
    /// <summary>Adds an outbox message on <paramref name="db"/> and saves; call inside the caller's transaction.</summary>
    public static async Task EnqueueAsync(DbContext db, Guid id, string tenantId, string type, int aggregateId,
        int attempt, string payload, CancellationToken ct, string? initiator = null)
    {
        var now = DateTime.UtcNow;
        var row = new OutboxMessage
        {
            Id = id, TenantId = tenantId, Type = type, AggregateId = aggregateId, Attempt = attempt,
            Payload = payload, CreatedAt = now, NextAttemptAt = now, Initiator = initiator
        };
        db.Set<OutboxMessage>().Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.Entry(row).State = EntityState.Detached;
        }
    }

    /// <summary>Adds an onboarding-requested message on <paramref name="db"/>; call inside the caller's transaction.</summary>
    public static Task EnqueueOnboardingAsync(DbContext db, Customer c, int attempt, CancellationToken ct, string? initiator = null)
    {
        var id = Guid.NewGuid();
        var dto = new CustomerSyncDto(1, c.TenantId, c.Id, attempt, id.ToString(), c.Kind, c.Name, c.Email, c.Phone, c.Status);
        return EnqueueAsync(db, id, c.TenantId, IntegrationNames.OnboardingRequested, c.Id, attempt,
            JsonSerializer.Serialize(dto), ct, initiator);
    }
}

public interface IWorkflowStarter
{
    /// <summary>Starts the workflow; returns normally when it was already started (idempotent).</summary>
    Task StartCustomerOnboardingAsync(CustomerSyncDto dto, CancellationToken ct, string? initiator = null);

    /// <summary>Starts CustomerToCashWorkflow, which owns the Java QuoteToOrderWorkflow child; idempotent per (tenant, quote).</summary>
    Task StartQuoteToOrderAsync(QuoteAcceptedDto dto, CancellationToken ct, string? initiator = null);

    /// <summary>Starts ReturnProcessingWorkflow; idempotent per (tenant, return).</summary>
    Task StartReturnProcessingAsync(ReturnRequestedDto dto, CancellationToken ct, string? initiator = null);
}

/// <summary>Publishes unpublished outbox rows to Temporal. At-least-once; workflow ids make it effectively once.</summary>
public sealed class OutboxPublisher(IDbContextFactory<FoundgineDbContext> factory, IOutboxRouter router,
    ILogger<OutboxPublisher> log)
{
    public async Task<int> PublishBatchAsync(int batchSize, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Claim a batch: FOR UPDATE SKIP LOCKED keeps concurrent publishers off each other's rows. EF has no
        // locking hint, so the claim is the one raw statement left; it still materialises the EF entity.
        var rows = await db.Set<OutboxMessage>()
            .FromSql($@"SELECT * FROM outbox_messages
                        WHERE published_at IS NULL AND next_attempt_at <= now()
                        ORDER BY created_at LIMIT {batchSize} FOR UPDATE SKIP LOCKED")
            .AsNoTracking().ToListAsync(ct);

        var published = 0;
        foreach (var row in rows)
        {
            try
            {
                await router.DispatchAsync(row, ct);

                var rowId = row.Id;
                var publishedAt = DateTime.UtcNow;
                await db.Set<OutboxMessage>().Where(m => m.Id == rowId)
                    .ExecuteUpdateAsync(u => u.SetProperty(m => m.PublishedAt, (DateTime?)publishedAt)
                        .SetProperty(m => m.LastError, (string?)null), ct);
                published++;
                Infrastructure.Telemetry.OutboxPublished.Add(1, new KeyValuePair<string, object?>("type", row.Type));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Outbox publish failed for {Id}", row.Id);
                Infrastructure.Telemetry.OutboxFailed.Add(1, new KeyValuePair<string, object?>("type", row.Type));
                var delaySeconds = Math.Min(300, 1 << Math.Min(row.PublishAttempts, 8));
                var failedId = row.Id;
                var next = DateTime.UtcNow.AddSeconds(delaySeconds);
                var error = ex.Message;
                await db.Set<OutboxMessage>().Where(m => m.Id == failedId)
                    .ExecuteUpdateAsync(u => u.SetProperty(m => m.PublishAttempts, m => m.PublishAttempts + 1)
                        .SetProperty(m => m.NextAttemptAt, next).SetProperty(m => m.LastError, (string?)error), ct);
            }
        }

        await tx.CommitAsync(ct);
        return published;
    }
}

public sealed class OutboxPublisherService(IServiceScopeFactory scopes, ILogger<OutboxPublisherService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var n = 0;
            try
            {
                using var scope = scopes.CreateScope();
                n = await scope.ServiceProvider.GetRequiredService<OutboxPublisher>().PublishBatchAsync(20, stop);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Outbox loop error");
            }

            if (n == 0) await Task.Delay(TimeSpan.FromSeconds(2), stop).ConfigureAwait(false);
        }
    }
}
