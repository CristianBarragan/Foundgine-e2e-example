using System.Text.Json;
using Crm.Api.Domain;
using Crm.Api.Integration;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Application;

public static class ShipmentStatuses
{
    public static readonly string[] All = ["Packed", "Shipped", "InTransit", "Delivered", "Cancelled"];
}

public static class InvoiceStatuses
{
    public static readonly string[] All = ["Issued", "PartiallyPaid", "Paid", "Overdue", "Void"];
}

public static class PaymentStatuses
{
    public static readonly string[] All = ["Received", "Reconciled", "Reversed"];
}

public static class ReturnStatuses
{
    public const string Requested = "Requested", Authorized = "Authorized", Rejected = "Rejected", Completed = "Completed";

    /// <summary>Requested -> Authorized | Rejected; Authorized -> Completed. Anything else is not allowed.</summary>
    public static bool CanMove(string from, string to) =>
        (from, to) is (Requested, Authorized) or (Requested, Rejected) or (Authorized, Completed);
}

public sealed record ReturnRequest(IReadOnlyList<ReturnLineDto>? Lines, string? Reason);

public sealed class ShipmentRow
{
    public string ErpShipmentId { get; set; } = "";
    public int QuoteId { get; set; }
    public string ErpOrderId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Carrier { get; set; } = "";
    public string TrackingNumber { get; set; } = "";
    public int Version { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class InvoiceRow
{
    public string ErpInvoiceId { get; set; } = "";
    public int QuoteId { get; set; }
    public int CustomerId { get; set; }
    public string ErpOrderId { get; set; } = "";
    public string Number { get; set; } = "";
    public decimal Total { get; set; }
    public string Status { get; set; } = "";
    public DateTime IssuedAt { get; set; }
    public DateTime? DueAt { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class PaymentRow
{
    public string ErpPaymentId { get; set; } = "";
    public string ErpInvoiceId { get; set; } = "";
    public int QuoteId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
    public int Version { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ReturnRow
{
    public int Id { get; set; }
    public int QuoteId { get; set; }
    public int CustomerId { get; set; }
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public string ErpReturnId { get; set; } = "";
    public string DecisionNote { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ReturnLineRow
{
    public int ReturnId { get; set; }
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
}

public sealed class SkuQuantityRow
{
    public string Sku { get; set; } = "";
    public long Quantity { get; set; }
}

public sealed record FulfilmentView(int QuoteId, string ErpOrderId, IReadOnlyList<ShipmentRow> Shipments,
    IReadOnlyList<InvoiceRow> Invoices, IReadOnlyList<PaymentRow> Payments);

public sealed record ReturnView(ReturnRow Return, IReadOnlyList<ReturnLineRow> Lines);

/// <summary>
/// Phase 8, CRM side. Shipment / invoice / payment facts are ERP-owned: the Record* methods (called by Temporal
/// activities with an explicit tenant) upsert them by ERP id and only accept a strictly newer version, so duplicate
/// or out-of-order deliveries are harmless. The ERP order id is resolved to a CRM quote; if the quote has not been
/// marked Ordered yet the call throws not-found and the activity retries. Return requests are CRM-owned: they are
/// written together with a ReturnRequested outbox message, and the ERP decision comes back via RecordReturnDecision.
/// </summary>
public sealed class FulfilmentService(IFoundgineRepository<Quote> quotes, IFoundgineRepository<Customer> customers,
    IntegrationService integration, ITenantContext tenant)
{
    private DbContext Db => quotes.Context;

    // ---- ERP-owned facts (activities) -----------------------------------------------------------------------------

    public async Task RecordShipmentAsync(RecordShipmentInput i, CancellationToken ct)
    {
        Require(i.ErpShipmentId, "erpShipmentId");
        RequireStatus(i.Status, ShipmentStatuses.All, "shipment");
        RequireVersion(i.Version);
        var quoteId = await ResolveQuoteAsync(i.TenantId, i.ErpOrderId, ct);
        await Db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO shipments (tenant_id, erp_shipment_id, quote_id, erp_order_id, status, carrier, tracking_number, version, updated_at)
            VALUES ({i.TenantId}, {i.ErpShipmentId}, {quoteId}, {i.ErpOrderId}, {i.Status}, {i.Carrier ?? ""}, {i.TrackingNumber ?? ""}, {i.Version}, {DateTime.UtcNow})
            ON CONFLICT (tenant_id, erp_shipment_id) DO UPDATE
               SET status = EXCLUDED.status, carrier = EXCLUDED.carrier, tracking_number = EXCLUDED.tracking_number,
                   version = EXCLUDED.version, updated_at = EXCLUDED.updated_at
             WHERE shipments.version < EXCLUDED.version AND shipments.quote_id = EXCLUDED.quote_id", ct);
    }

    public async Task RecordInvoiceAsync(RecordInvoiceInput i, CancellationToken ct)
    {
        Require(i.ErpInvoiceId, "erpInvoiceId");
        Require(i.Number, "number");
        RequireStatus(i.Status, InvoiceStatuses.All, "invoice");
        RequireVersion(i.Version);
        if (i.Total < 0) throw new CrmValidationException("Invoice total cannot be negative.");
        var quoteId = await ResolveQuoteAsync(i.TenantId, i.ErpOrderId, ct);
        var q = await quotes.GetAsync(quoteId, ct) ?? throw new CrmNotFoundException($"Quote {quoteId} not found.");
        await Db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO invoices (tenant_id, erp_invoice_id, quote_id, customer_id, erp_order_id, number, total, status, issued_at, due_at, version, updated_at)
            VALUES ({i.TenantId}, {i.ErpInvoiceId}, {quoteId}, {q.CustomerId}, {i.ErpOrderId}, {i.Number}, {i.Total}, {i.Status}, {i.IssuedAt.ToUniversalTime()}, {i.DueAt?.ToUniversalTime()}, {i.Version}, {DateTime.UtcNow})
            ON CONFLICT (tenant_id, erp_invoice_id) DO UPDATE
               SET number = EXCLUDED.number, total = EXCLUDED.total, status = EXCLUDED.status, issued_at = EXCLUDED.issued_at,
                   due_at = EXCLUDED.due_at, version = EXCLUDED.version, updated_at = EXCLUDED.updated_at
             WHERE invoices.version < EXCLUDED.version AND invoices.quote_id = EXCLUDED.quote_id", ct);
    }

    public async Task RecordPaymentAsync(RecordPaymentInput i, CancellationToken ct)
    {
        Require(i.ErpPaymentId, "erpPaymentId");
        Require(i.ErpInvoiceId, "erpInvoiceId");
        RequireStatus(i.Status, PaymentStatuses.All, "payment");
        RequireVersion(i.Version);
        if (i.Amount < 0) throw new CrmValidationException("Payment amount cannot be negative.");
        var quoteId = await ResolveQuoteAsync(i.TenantId, i.ErpOrderId, ct);
        await Db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO payments (tenant_id, erp_payment_id, erp_invoice_id, quote_id, amount, status, version, updated_at)
            VALUES ({i.TenantId}, {i.ErpPaymentId}, {i.ErpInvoiceId}, {quoteId}, {i.Amount}, {i.Status}, {i.Version}, {DateTime.UtcNow})
            ON CONFLICT (tenant_id, erp_payment_id) DO UPDATE
               SET amount = EXCLUDED.amount, status = EXCLUDED.status, version = EXCLUDED.version, updated_at = EXCLUDED.updated_at
             WHERE payments.version < EXCLUDED.version AND payments.quote_id = EXCLUDED.quote_id", ct);
    }

    // ---- Reads (current tenant) -----------------------------------------------------------------------------------

    public async Task<FulfilmentView> GetFulfilmentAsync(int quoteId, CancellationToken ct)
    {
        var q = await GetQuoteAsync(quoteId, ct);
        var t = q.TenantId;
        var shipments = await Db.Database.SqlQuery<ShipmentRow>($@"
            SELECT erp_shipment_id AS ""ErpShipmentId"", quote_id AS ""QuoteId"", erp_order_id AS ""ErpOrderId"", status AS ""Status"",
                   carrier AS ""Carrier"", tracking_number AS ""TrackingNumber"", version AS ""Version"", updated_at AS ""UpdatedAt""
            FROM shipments WHERE tenant_id = {t} AND quote_id = {quoteId} ORDER BY erp_shipment_id").ToListAsync(ct);
        var invoices = await QueryInvoicesAsync(t, quoteId, null, ct);
        var payments = await Db.Database.SqlQuery<PaymentRow>($@"
            SELECT erp_payment_id AS ""ErpPaymentId"", erp_invoice_id AS ""ErpInvoiceId"", quote_id AS ""QuoteId"", amount AS ""Amount"",
                   status AS ""Status"", version AS ""Version"", updated_at AS ""UpdatedAt""
            FROM payments WHERE tenant_id = {t} AND quote_id = {quoteId} ORDER BY erp_payment_id").ToListAsync(ct);
        return new FulfilmentView(q.Id, q.ErpOrderId, shipments, invoices, payments);
    }

    public async Task<IReadOnlyList<InvoiceRow>> ListInvoicesForCustomerAsync(int customerId, CancellationToken ct)
    {
        var c = await customers.GetAsync(customerId, ct);
        if (c is null || c.TenantId != tenant.TenantId) throw new CrmNotFoundException($"Customer {customerId} not found.");
        return await QueryInvoicesAsync(c.TenantId, null, c.Id, ct);
    }

    // 0 means "any": identity ids are always > 0, and it avoids untyped NULL parameters in the SQL.
    private Task<List<InvoiceRow>> QueryInvoicesAsync(string t, int? quote, int? customer, CancellationToken ct)
    {
        int quoteId = quote ?? 0, customerId = customer ?? 0;
        return Db.Database.SqlQuery<InvoiceRow>($@"
            SELECT erp_invoice_id AS ""ErpInvoiceId"", quote_id AS ""QuoteId"", customer_id AS ""CustomerId"", erp_order_id AS ""ErpOrderId"",
                   number AS ""Number"", total AS ""Total"", status AS ""Status"", issued_at AS ""IssuedAt"", due_at AS ""DueAt"",
                   version AS ""Version"", updated_at AS ""UpdatedAt""
            FROM invoices
            WHERE tenant_id = {t} AND ({quoteId} = 0 OR quote_id = {quoteId}) AND ({customerId} = 0 OR customer_id = {customerId})
            ORDER BY issued_at DESC, erp_invoice_id").ToListAsync(ct);
    }

    // ---- Returns --------------------------------------------------------------------------------------------------

    public async Task<ReturnView> RequestReturnAsync(int quoteId, ReturnRequest r, CancellationToken ct)
    {
        var q = await GetQuoteAsync(quoteId, ct);
        if (q.Status != QuoteStatuses.Ordered) throw new CrmConflictException("Only an ordered quote can have returns.");
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500) throw new CrmValidationException("A reason (up to 500 characters) is required.");
        var wanted = MergeLines(r.Lines);
        var mapping = await integration.GetMappingAsync(q.TenantId, q.CustomerId, ct);
        if (mapping is not { Status: "Active" } || string.IsNullOrEmpty(mapping.ErpCustomerId))
            throw new CrmConflictException("The customer is not yet synchronised to the ERP.");

        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        // Serialise concurrent return requests for the same quote so the quantity cap below cannot be raced.
        await Db.Database.SqlQuery<int>($@"SELECT 1 AS ""Value"" FROM quotes WHERE quote_id = {quoteId} AND tenant_id = {q.TenantId} FOR UPDATE")
            .ToListAsync(ct);
        var quoted = (await Db.Database.SqlQuery<SkuQuantityRow>($@"
            SELECT sku AS ""Sku"", SUM(quantity)::bigint AS ""Quantity"" FROM quote_lines
            WHERE quote_id = {quoteId} AND tenant_id = {q.TenantId} GROUP BY sku").ToListAsync(ct)).ToDictionary(x => x.Sku, x => x.Quantity);
        var already = (await Db.Database.SqlQuery<SkuQuantityRow>($@"
            SELECT l.sku AS ""Sku"", SUM(l.quantity)::bigint AS ""Quantity"" FROM return_request_lines l
            JOIN return_requests r ON r.return_id = l.return_id
            WHERE r.quote_id = {quoteId} AND r.tenant_id = {q.TenantId} AND r.status <> 'Rejected' GROUP BY l.sku").ToListAsync(ct))
            .ToDictionary(x => x.Sku, x => x.Quantity);
        foreach (var (sku, qty) in wanted)
        {
            if (!quoted.TryGetValue(sku, out var ordered)) throw new CrmValidationException($"SKU {sku} is not on this quote.");
            if (already.GetValueOrDefault(sku) + qty > ordered)
                throw new CrmValidationException($"Cannot return {qty} of {sku}: only {ordered - already.GetValueOrDefault(sku)} remaining.");
        }

        var now = DateTime.UtcNow;
        var returnId = await Db.Database.SqlQuery<int>($@"
            INSERT INTO return_requests (tenant_id, quote_id, customer_id, status, reason, requested_by, created_at, updated_at)
            VALUES ({q.TenantId}, {quoteId}, {q.CustomerId}, 'Requested', {r.Reason.Trim()}, {tenant.UserId}, {now}, {now})
            RETURNING return_id AS ""Value""").SingleAsync(ct);
        foreach (var (sku, qty) in wanted)
            await Db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO return_request_lines (tenant_id, return_id, sku, quantity) VALUES ({q.TenantId}, {returnId}, {sku}, {qty})", ct);

        var messageId = Guid.NewGuid();
        var dto = new ReturnRequestedDto(1, q.TenantId, returnId, quoteId, q.CustomerId, mapping.ErpCustomerId!, q.ErpOrderId,
            messageId.ToString(), r.Reason.Trim(), wanted.Select(w => new ReturnLineDto(w.Key, w.Value)).ToArray());
        var payload = JsonSerializer.Serialize(dto);
        await Db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO outbox_messages (id, tenant_id, type, aggregate_id, attempt, payload, created_at)
            VALUES ({messageId}, {q.TenantId}, {FulfilmentIntegrationNames.ReturnRequested}, {returnId}, 1, {payload}::jsonb, {now})", ct);
        await tx.CommitAsync(ct);
        return await GetReturnAsync(returnId, ct);
    }

    public async Task<ReturnView> GetReturnAsync(int returnId, CancellationToken ct)
    {
        var t = tenant.TenantId;
        var row = (await QueryReturnsAsync(t, null, returnId, ct)).FirstOrDefault()
                  ?? throw new CrmNotFoundException($"Return {returnId} not found.");
        return new ReturnView(row, await QueryReturnLinesAsync(t, [returnId], ct));
    }

    public async Task<IReadOnlyList<ReturnView>> ListReturnsForQuoteAsync(int quoteId, CancellationToken ct)
    {
        var q = await GetQuoteAsync(quoteId, ct);
        var rows = await QueryReturnsAsync(q.TenantId, quoteId, null, ct);
        var lines = await QueryReturnLinesAsync(q.TenantId, rows.Select(r => r.Id).ToArray(), ct);
        return rows.Select(r => new ReturnView(r, lines.Where(l => l.ReturnId == r.Id).ToList())).ToList();
    }

    /// <summary>
    /// Called by ReturnProcessingWorkflow. Same decision again is a no-op; an illegal transition is a conflict.
    /// Authorized records the ERP return id (stock/financial adjustment is owned and executed by the ERP).
    /// </summary>
    public async Task RecordReturnDecisionAsync(RecordReturnDecisionInput i, CancellationToken ct)
    {
        if (i.Decision is not (ReturnStatuses.Authorized or ReturnStatuses.Rejected or ReturnStatuses.Completed))
            throw new CrmValidationException("Decision must be Authorized, Rejected or Completed.");
        if (i.Decision == ReturnStatuses.Authorized && string.IsNullOrWhiteSpace(i.ErpReturnId))
            throw new CrmValidationException("erpReturnId is required when authorizing.");
        var current = (await QueryReturnsAsync(i.TenantId, null, i.ReturnId, ct)).FirstOrDefault()
                      ?? throw new CrmNotFoundException($"Return {i.ReturnId} not found.");
        if (current.Status == i.Decision)
        {
            if (i.Decision == ReturnStatuses.Authorized && current.ErpReturnId != i.ErpReturnId)
                throw new CrmConflictException("The return is already linked to a different ERP return.");
            return;
        }

        if (!ReturnStatuses.CanMove(current.Status, i.Decision))
            throw new CrmConflictException($"A {current.Status} return cannot become {i.Decision}.");
        var erpId = i.ErpReturnId ?? "";
        var note = (i.Note ?? "").Length > 500 ? i.Note![..500] : i.Note ?? "";
        var changed = await Db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE return_requests
            SET status = {i.Decision}, updated_at = {DateTime.UtcNow}, decision_note = {note},
                erp_return_id = CASE WHEN {erpId} <> '' THEN {erpId} ELSE erp_return_id END
            WHERE return_id = {i.ReturnId} AND tenant_id = {i.TenantId} AND status = {current.Status}", ct);
        if (changed != 1) throw new CrmConflictException("The return changed concurrently; retry.");
    }

    // ---- helpers --------------------------------------------------------------------------------------------------

    private async Task<int> ResolveQuoteAsync(string tenantId, string erpOrderId, CancellationToken ct)
    {
        Require(tenantId, "tenantId");
        Require(erpOrderId, "erpOrderId");
        var ids = await Db.Database.SqlQuery<int>($@"
            SELECT quote_id AS ""Value"" FROM quotes WHERE tenant_id = {tenantId} AND erp_order_id = {erpOrderId}").ToListAsync(ct);
        return ids.Count == 1 ? ids[0] : throw new CrmNotFoundException($"No CRM quote for ERP order {erpOrderId}.");
    }

    private async Task<Quote> GetQuoteAsync(int id, CancellationToken ct)
    {
        var q = await quotes.GetAsync(id, ct);
        return q is not null && q.TenantId == tenant.TenantId ? q : throw new CrmNotFoundException($"Quote {id} not found.");
    }

    private Task<List<ReturnRow>> QueryReturnsAsync(string t, int? quote, int? ret, CancellationToken ct)
    {
        int quoteId = quote ?? 0, returnId = ret ?? 0;
        return Db.Database.SqlQuery<ReturnRow>($@"
            SELECT return_id AS ""Id"", quote_id AS ""QuoteId"", customer_id AS ""CustomerId"", status AS ""Status"", reason AS ""Reason"",
                   erp_return_id AS ""ErpReturnId"", decision_note AS ""DecisionNote"", requested_by AS ""RequestedBy"",
                   created_at AS ""CreatedAt"", updated_at AS ""UpdatedAt""
            FROM return_requests
            WHERE tenant_id = {t} AND ({quoteId} = 0 OR quote_id = {quoteId}) AND ({returnId} = 0 OR return_id = {returnId})
            ORDER BY return_id").ToListAsync(ct);
    }

    private async Task<IReadOnlyList<ReturnLineRow>> QueryReturnLinesAsync(string t, int[] returnIds, CancellationToken ct) =>
        returnIds.Length == 0
            ? []
            : await Db.Database.SqlQuery<ReturnLineRow>($@"
                SELECT return_id AS ""ReturnId"", sku AS ""Sku"", quantity AS ""Quantity""
                FROM return_request_lines WHERE tenant_id = {t} AND return_id = ANY({returnIds}) ORDER BY return_line_id").ToListAsync(ct);

    private static SortedDictionary<string, int> MergeLines(IReadOnlyList<ReturnLineDto>? lines)
    {
        if (lines is null || lines.Count is 0 or > 100) throw new CrmValidationException("A return needs 1 to 100 lines.");
        var merged = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l.Sku) || l.Sku.Length > 64) throw new CrmValidationException("Each line needs a SKU.");
            if (l.Quantity <= 0) throw new CrmValidationException("Quantity must be positive.");
            var sku = l.Sku.Trim();
            merged[sku] = checked(merged.GetValueOrDefault(sku) + l.Quantity);
        }

        return merged;
    }

    private static void Require(string? v, string name)
    {
        if (string.IsNullOrWhiteSpace(v)) throw new CrmValidationException($"{name} is required.");
    }

    private static void RequireVersion(int v)
    {
        if (v <= 0) throw new CrmValidationException("version must be positive.");
    }

    private static void RequireStatus(string? v, string[] allowed, string what)
    {
        if (v is null || !allowed.Contains(v)) throw new CrmValidationException($"Invalid {what} status.");
    }
}
