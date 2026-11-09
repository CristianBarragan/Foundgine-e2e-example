using System.Text.Json;
using Foundgine.Orchestration.Workflows.Contracts;
using Foundgine.Orchestration.Workflows.Workflows;
using Npgsql;
using Temporalio.Client;
namespace Foundgine.Crm.OutboxDispatcher;
public sealed record OutboxDispatcherOptions(string TaskQueue, string DispatcherId, TimeSpan PollInterval, TimeSpan LeaseDuration, int BatchSize);
internal sealed record OutboxMessage(Guid EventId, string TenantId, string EventType, string AggregateId, string Payload);
public sealed class OutboxDispatchService(NpgsqlDataSource db, TemporalClient temporal, OutboxDispatcherOptions options, ILogger<OutboxDispatchService> log) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var msg in await ClaimAsync(ct))
                {
                    try { await DispatchAsync(msg); await MarkAsync(msg.EventId, true, null, ct); }
                    catch (Exception ex) { log.LogError(ex, "Outbox dispatch failed for {EventId}", msg.EventId); await MarkAsync(msg.EventId, false, ex.ToString(), ct); }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Outbox polling failed"); }
            await Task.Delay(options.PollInterval, ct);
        }
    }
    private async Task<IReadOnlyList<OutboxMessage>> ClaimAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        const string sql = """
        WITH candidates AS (
          SELECT event_id FROM integration_outbox
          WHERE dispatched_at IS NULL AND next_attempt_at <= now()
            AND (lease_until IS NULL OR lease_until < now())
            AND event_type = 'CommercialOrderAccepted'
          ORDER BY occurred_at FOR UPDATE SKIP LOCKED LIMIT @batch
        )
        UPDATE integration_outbox o SET lease_owner=@owner,
          lease_until=now()+@lease, attempt_count=attempt_count+1
        FROM candidates c WHERE o.event_id=c.event_id
        RETURNING o.event_id,o.tenant_id,o.event_type,o.aggregate_id,o.payload::text;
        """;
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("batch", options.BatchSize);
        cmd.Parameters.AddWithValue("owner", options.DispatcherId);
        cmd.Parameters.AddWithValue("lease", options.LeaseDuration);
        var rows = new List<OutboxMessage>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) rows.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        await tx.CommitAsync(ct);
        return rows;
    }
    private async Task DispatchAsync(OutboxMessage m)
    {
        var input = JsonSerializer.Deserialize<CommercialOrderWorkflowInput>(m.Payload, JsonOptions)
            ?? throw new InvalidOperationException("Outbox payload is empty.");
        if (input.TenantId != m.TenantId || input.CrmCommercialOrderId != m.AggregateId)
            throw new InvalidOperationException("Outbox tenant/aggregate does not match workflow payload.");
        var id = $"commercial-order-fulfillment:{m.TenantId}:{m.AggregateId}";
        try
        {
            await temporal.StartWorkflowAsync((CommercialOrderFulfillmentWorkflow wf) => wf.RunAsync(input),
                new(id: id, taskQueue: options.TaskQueue) {
                    IdConflictPolicy = Temporalio.Api.Enums.V1.WorkflowIdConflictPolicy.UseExisting,
                    IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.AllowDuplicateFailedOnly
                });
            log.LogInformation("Temporal workflow {WorkflowId} started/confirmed for event {EventId}", id, m.EventId);
        }
        catch (Temporalio.Exceptions.WorkflowAlreadyStartedException)
        {
            // Crash window: the workflow was started (and has since completed or been rejected) before the
            // outbox row was acknowledged. The ID is deterministic per tenant+order, so the process already
            // exists; retrying would loop forever. Treat as delivered. Verify history out-of-band if needed.
            log.LogWarning("Workflow {WorkflowId} already exists in a terminal state; marking event {EventId} delivered", id, m.EventId);
        }
    }
    private async Task MarkAsync(Guid eventId, bool dispatched, string? error, CancellationToken ct)
    {
        var sql = dispatched
          ? "UPDATE integration_outbox SET dispatched_at=now(),lease_owner=NULL,lease_until=NULL,last_error=NULL WHERE event_id=@id AND lease_owner=@owner"
          : """UPDATE integration_outbox SET lease_owner=NULL,lease_until=NULL,last_error=left(@error,4000),
               next_attempt_at=now()+make_interval(secs => least(300,power(2,least(attempt_count,8))::int))
               WHERE event_id=@id AND lease_owner=@owner""";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", eventId); cmd.Parameters.AddWithValue("owner", options.DispatcherId);
        if (!dispatched) cmd.Parameters.AddWithValue("error", error ?? "unknown");
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
