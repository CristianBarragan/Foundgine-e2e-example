using System.Text.Json;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Integration;

public static class Outbox
{
    /// <summary>Adds an onboarding-requested message on <paramref name="db"/>; call inside the caller's transaction.</summary>
    public static async Task EnqueueOnboardingAsync(DbContext db, Domain.Customer c, int attempt, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var dto = new CustomerSyncDto(1, c.TenantId, c.Id, attempt, id.ToString(), c.Kind, c.Name, c.Email, c.Phone, c.Status);
        var payload = JsonSerializer.Serialize(dto);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO outbox_messages (id, tenant_id, type, aggregate_id, attempt, payload, created_at)
            VALUES ({id}, {c.TenantId}, {IntegrationNames.OnboardingRequested}, {c.Id}, {attempt}, {payload}::jsonb, {DateTime.UtcNow})", ct);
    }
}

public sealed class OutboxRow
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public int PublishAttempts { get; set; }
}

public interface IWorkflowStarter
{
    /// <summary>Starts the workflow; returns normally when it was already started (idempotent).</summary>
    Task StartCustomerOnboardingAsync(CustomerSyncDto dto, CancellationToken ct);

    /// <summary>Starts QuoteToOrderWorkflow; idempotent per (tenant, quote).</summary>
    Task StartQuoteToOrderAsync(QuoteAcceptedDto dto, CancellationToken ct);

    /// <summary>Starts ReturnProcessingWorkflow; idempotent per (tenant, return).</summary>
    Task StartReturnProcessingAsync(ReturnRequestedDto dto, CancellationToken ct);
}

/// <summary>Publishes unpublished outbox rows to Temporal. At-least-once; workflow ids make it effectively once.</summary>
public sealed class OutboxPublisher(IDbContextFactory<FoundgineDbContext> factory, IWorkflowStarter starter,
    ILogger<OutboxPublisher> log)
{
    public async Task<int> PublishBatchAsync(int batchSize, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Database.SqlQuery<OutboxRow>($@"
            SELECT id AS ""Id"", type AS ""Type"", payload::text AS ""Payload"", publish_attempts AS ""PublishAttempts""
            FROM outbox_messages
            WHERE published_at IS NULL AND next_attempt_at <= now()
            ORDER BY created_at LIMIT {batchSize} FOR UPDATE SKIP LOCKED").ToListAsync(ct);

        var published = 0;
        foreach (var row in rows)
        {
            try
            {
                switch (row.Type)
                {
                    case IntegrationNames.OnboardingRequested:
                        await starter.StartCustomerOnboardingAsync(JsonSerializer.Deserialize<CustomerSyncDto>(row.Payload)!, ct);
                        break;
                    case QuoteIntegrationNames.QuoteAccepted:
                        await starter.StartQuoteToOrderAsync(JsonSerializer.Deserialize<QuoteAcceptedDto>(row.Payload)!, ct);
                        break;
                    case FulfilmentIntegrationNames.ReturnRequested:
                        await starter.StartReturnProcessingAsync(JsonSerializer.Deserialize<ReturnRequestedDto>(row.Payload)!, ct);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown outbox type {row.Type}");
                }

                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE outbox_messages SET published_at = now(), last_error = NULL WHERE id = {row.Id}", ct);
                published++;
                Infrastructure.Telemetry.OutboxPublished.Add(1, new KeyValuePair<string, object?>("type", row.Type));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Outbox publish failed for {Id}", row.Id);
                Infrastructure.Telemetry.OutboxFailed.Add(1, new KeyValuePair<string, object?>("type", row.Type));
                var delaySeconds = Math.Min(300, 1 << Math.Min(row.PublishAttempts, 8));
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE outbox_messages SET publish_attempts = publish_attempts + 1,
                        next_attempt_at = now() + make_interval(secs => {delaySeconds}), last_error = {ex.Message}
                    WHERE id = {row.Id}", ct);
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
