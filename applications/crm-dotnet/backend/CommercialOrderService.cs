using Foundgine.Providers.Outbox;
using Npgsql;
using NpgsqlTypes;

namespace Foundgine.Crm.Api;

public enum CallbackOutcome { Applied, Replayed, NotFound, Conflict }

/// <summary>
/// CRM-owned commercial-order persistence. This deliberately uses one explicit Npgsql transaction
/// instead of the Foundgine mutation provider: order acceptance and the foundgine_outbox insert must
/// commit atomically, and the provider cannot join a caller-owned transaction. Because this path
/// bypasses the Foundgine executor's tenant predicate, EVERY statement below carries an explicit
/// tenant_id condition; keep it that way when editing.
/// </summary>
public sealed class CommercialOrderService(NpgsqlDataSource db, PostgresOutbox outbox)
{
    /// <summary>Outbox topic routed to the Temporal process <see cref="FulfillmentProcessName"/> by the dispatcher host.</summary>
    public const string AcceptedEventType = "CommercialOrderAccepted";
    public const string FulfillmentProcessName = "commercial-order-fulfillment";

    public async Task<(CommercialOrder Order, bool Replayed)> CreateAsync(
        CrmScope scope, string? clientKey, CreateCommercialOrder input, CancellationToken ct)
    {
        var key = CommercialOrderValidation.NormalizeIdempotencyKey(clientKey);
        var request = CommercialOrderValidation.Normalize(input);
        var fingerprint = CommercialOrderValidation.Fingerprint(request);

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var check = new NpgsqlCommand(
            "SELECT 1 FROM crm_companies WHERE tenant_id = @tenant AND id = @company", conn, tx))
        {
            check.Parameters.AddWithValue("tenant", scope.TenantId);
            check.Parameters.AddWithValue("company", request.CompanyId);
            if (await check.ExecuteScalarAsync(ct) is null)
                throw new CrmValidationException("Company does not exist in this tenant.");
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var orderNumber = $"CO-{now:yyyyMMdd}-{id:N}".ToUpperInvariant()[..20];

        bool inserted;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO crm_commercial_orders
              (id, tenant_id, order_number, company_id, currency, status, client_request_key, request_fingerprint, created_at, updated_at)
            VALUES (@id, @tenant, @number, @company, @currency, 'Accepted', @key, @fp, @now, @now)
            ON CONFLICT (tenant_id, client_request_key) DO NOTHING
            RETURNING id
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("tenant", scope.TenantId);
            insert.Parameters.AddWithValue("number", orderNumber);
            insert.Parameters.AddWithValue("company", request.CompanyId);
            insert.Parameters.AddWithValue("currency", request.Currency);
            insert.Parameters.AddWithValue("key", key);
            insert.Parameters.AddWithValue("fp", fingerprint);
            insert.Parameters.AddWithValue("now", now);
            inserted = await insert.ExecuteScalarAsync(ct) is not null;
        }

        if (!inserted)
        {
            // Same key already used in this tenant: replay if it was the same request, otherwise conflict.
            Guid existingId; string existingFingerprint;
            await using (var read = new NpgsqlCommand(
                "SELECT id, request_fingerprint FROM crm_commercial_orders WHERE tenant_id = @tenant AND client_request_key = @key", conn, tx))
            {
                read.Parameters.AddWithValue("tenant", scope.TenantId);
                read.Parameters.AddWithValue("key", key);
                await using var r = await read.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) throw new InvalidOperationException("Idempotent order vanished after conflict.");
                existingId = r.GetGuid(0);
                existingFingerprint = r.GetString(1).Trim();
            }
            await tx.RollbackAsync(ct);
            if (!string.Equals(existingFingerprint, fingerprint, StringComparison.Ordinal))
                throw new CommercialOrderIdempotencyConflictException("Idempotency-Key was already used for a different request.");
            var existing = await GetAsync(scope, existingId, ct)
                ?? throw new InvalidOperationException("Idempotent order could not be reloaded.");
            return (existing, true);
        }

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO crm_commercial_order_lines (tenant_id, order_id, line_no, product_id, quantity, unit_of_measure)
                VALUES (@tenant, @order, @line, @product, @qty, @uom)
                """, conn, tx);
            cmd.Parameters.AddWithValue("tenant", scope.TenantId);
            cmd.Parameters.AddWithValue("order", id);
            cmd.Parameters.AddWithValue("line", i + 1);
            cmd.Parameters.AddWithValue("product", line.ProductId);
            cmd.Parameters.AddWithValue("qty", line.Quantity);
            cmd.Parameters.AddWithValue("uom", line.UnitOfMeasure);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Same transaction as the order: the event exists if and only if the order is durable.
        var payload = CommercialOrderValidation.BuildPayload(scope.TenantId, id, orderNumber, request);
        // Message id == order id: one accepted event per order, so the Temporal business key (message id) is stable
        // and redelivery maps to the same workflow. Enqueue is idempotent by (tenant, id) and joins this transaction.
        await outbox.EnqueueAsync(tx, new FoundgineOutboxMessage(
            Id: id,
            TenantId: scope.TenantId,
            Topic: AcceptedEventType,
            PayloadJson: CommercialOrderValidation.SerializePayload(payload),
            Key: id.ToString("N"),
            Headers: new Dictionary<string, string> { ["initiator"] = "system:crm" }), ct);

        await tx.CommitAsync(ct);

        return (new CommercialOrder
        {
            Id = id, TenantId = scope.TenantId, OrderNumber = orderNumber, CompanyId = request.CompanyId,
            Currency = request.Currency, Status = CommercialOrderStatus.Accepted,
            CreatedAt = now, UpdatedAt = now, Lines = request.Lines
        }, false);
    }

    public async Task<CommercialOrder?> GetAsync(CrmScope scope, Guid id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var order = await ReadOrdersAsync(conn, "AND id = @id", scope.TenantId, id, 1, ct);
        return order.FirstOrDefault();
    }

    public async Task<IReadOnlyList<CommercialOrder>> ListAsync(CrmScope scope, int limit, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await ReadOrdersAsync(conn, "", scope.TenantId, null, Math.Clamp(limit, 1, 100), ct);
    }

    private static async Task<IReadOnlyList<CommercialOrder>> ReadOrdersAsync(
        NpgsqlConnection conn, string extraFilter, string tenantId, Guid? id, int limit, CancellationToken ct)
    {
        var orders = new List<CommercialOrder>();
        await using (var cmd = new NpgsqlCommand($"""
            SELECT id, order_number, company_id, currency, status, failure_reason, erp_reference, created_at, updated_at
            FROM crm_commercial_orders WHERE tenant_id = @tenant {extraFilter}
            ORDER BY created_at DESC LIMIT @limit
            """, conn))
        {
            cmd.Parameters.AddWithValue("tenant", tenantId);
            cmd.Parameters.AddWithValue("limit", limit);
            if (id is { } value) cmd.Parameters.AddWithValue("id", value);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                orders.Add(new CommercialOrder
                {
                    Id = r.GetGuid(0), TenantId = tenantId, OrderNumber = r.GetString(1), CompanyId = r.GetGuid(2),
                    Currency = r.GetString(3).Trim(), Status = r.GetString(4),
                    FailureReason = r.IsDBNull(5) ? null : r.GetString(5),
                    ErpReference = r.IsDBNull(6) ? null : r.GetString(6),
                    CreatedAt = r.GetFieldValue<DateTimeOffset>(7), UpdatedAt = r.GetFieldValue<DateTimeOffset>(8)
                });
        }
        if (orders.Count == 0) return orders;

        var byId = orders.ToDictionary(o => o.Id);
        var lines = orders.ToDictionary(o => o.Id, _ => new List<CommercialOrderLineInput>());
        await using (var cmd = new NpgsqlCommand("""
            SELECT order_id, product_id, quantity, unit_of_measure FROM crm_commercial_order_lines
            WHERE tenant_id = @tenant AND order_id = ANY(@ids) ORDER BY order_id, line_no
            """, conn))
        {
            cmd.Parameters.AddWithValue("tenant", tenantId);
            cmd.Parameters.AddWithValue("ids", byId.Keys.ToArray());
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                lines[r.GetGuid(0)].Add(new CommercialOrderLineInput(r.GetString(1), r.GetDecimal(2), r.GetString(3)));
        }
        foreach (var order in orders) order.Lines = lines[order.Id];
        return orders;
    }

    /// <summary>
    /// Applies an orchestration outcome. Safe under Temporal retries: the order row is locked, the
    /// (tenant, idempotency key) pair is recorded once, and re-delivery of the same command is a no-op.
    /// </summary>
    public async Task<CallbackOutcome> ApplyFulfillmentAsync(
        Guid orderId, FulfillmentCallback callback, string targetStatus, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        string? current;
        await using (var lockRow = new NpgsqlCommand(
            "SELECT status FROM crm_commercial_orders WHERE tenant_id = @tenant AND id = @id FOR UPDATE", conn, tx))
        {
            lockRow.Parameters.AddWithValue("tenant", callback.TenantId);
            lockRow.Parameters.AddWithValue("id", orderId);
            current = await lockRow.ExecuteScalarAsync(ct) as string;
        }
        if (current is null) { await tx.RollbackAsync(ct); return CallbackOutcome.NotFound; }

        bool firstDelivery;
        await using (var inbox = new NpgsqlCommand("""
            INSERT INTO crm_integration_inbox (tenant_id, idempotency_key, order_id, outcome)
            VALUES (@tenant, @key, @id, @outcome)
            ON CONFLICT (tenant_id, idempotency_key) DO NOTHING
            RETURNING 1
            """, conn, tx))
        {
            inbox.Parameters.AddWithValue("tenant", callback.TenantId);
            inbox.Parameters.AddWithValue("key", callback.IdempotencyKey);
            inbox.Parameters.AddWithValue("id", orderId);
            inbox.Parameters.AddWithValue("outcome", targetStatus);
            firstDelivery = await inbox.ExecuteScalarAsync(ct) is not null;
        }

        if (!firstDelivery)
        {
            string? priorOutcome; Guid priorOrder;
            await using (var read = new NpgsqlCommand(
                "SELECT outcome, order_id FROM crm_integration_inbox WHERE tenant_id = @tenant AND idempotency_key = @key", conn, tx))
            {
                read.Parameters.AddWithValue("tenant", callback.TenantId);
                read.Parameters.AddWithValue("key", callback.IdempotencyKey);
                await using var r = await read.ExecuteReaderAsync(ct);
                await r.ReadAsync(ct);
                priorOutcome = r.GetString(0); priorOrder = r.GetGuid(1);
            }
            await tx.RollbackAsync(ct);
            return priorOrder == orderId && priorOutcome == targetStatus ? CallbackOutcome.Replayed : CallbackOutcome.Conflict;
        }

        if (current == targetStatus) { await tx.CommitAsync(ct); return CallbackOutcome.Replayed; }
        if (current != CommercialOrderStatus.Accepted) { await tx.RollbackAsync(ct); return CallbackOutcome.Conflict; }

        await using (var update = new NpgsqlCommand("""
            UPDATE crm_commercial_orders
            SET status = @status, updated_at = now(),
                erp_reference = CASE WHEN @status = 'FulfillmentConfirmed' THEN @external ELSE erp_reference END,
                failure_reason = CASE WHEN @status = 'FulfillmentFailed' THEN @reason ELSE failure_reason END
            WHERE tenant_id = @tenant AND id = @id AND status = 'Accepted'
            """, conn, tx))
        {
            // Explicit types: an untyped NULL parameter inside CASE makes PostgreSQL fail type inference.
            update.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Text) { Value = targetStatus });
            update.Parameters.Add(new NpgsqlParameter("external", NpgsqlDbType.Text)
                { Value = string.IsNullOrEmpty(callback.ExternalId) ? (object)DBNull.Value : callback.ExternalId });
            update.Parameters.Add(new NpgsqlParameter("reason", NpgsqlDbType.Text)
                { Value = string.IsNullOrEmpty(callback.Reason) ? (object)DBNull.Value : callback.Reason });
            update.Parameters.AddWithValue("tenant", callback.TenantId);
            update.Parameters.AddWithValue("id", orderId);
            await update.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return CallbackOutcome.Applied;
    }
}
