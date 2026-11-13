using System.Text.Json;
using Crm.Api.Domain;
using Crm.Api.Infrastructure;
using Crm.Api.Integration;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QuoteLine = Crm.Api.Domain.QuoteLine;

namespace Crm.Api.Application;

public static class ShipmentStatuses
{
    public static readonly string[] All = ["Packed", "Shipped", "InTransit", "Delivered", "Cancelled"];
}

public static class InvoiceStatuses
{
    public const string Issued = "Issued", PartiallyPaid = "PartiallyPaid", Paid = "Paid", Overdue = "Overdue", Void = "Void";
    public static readonly string[] All = [Issued, PartiallyPaid, Paid, Overdue, Void];
}

public static class PaymentStatuses
{
    public const string Received = "Received", Reconciled = "Reconciled", Reversed = "Reversed";
    public static readonly string[] All = [Received, Reconciled, Reversed];
}

public sealed record ReturnRequestInput(IReadOnlyList<ReturnLineDto>? Lines, string? Reason);

public sealed record FulfilmentView(int QuoteId, string ErpOrderId, IReadOnlyList<Shipment> Shipments,
    IReadOnlyList<Invoice> Invoices, IReadOnlyList<Payment> Payments);

public sealed record ReturnView(ReturnRequest Return, IReadOnlyList<ReturnRequestLine> Lines);

/// <summary>
/// Phase 8, CRM side. Shipment / invoice / payment facts are ERP-owned: the Record* methods (called by Temporal
/// activities with an explicit tenant) upsert them by ERP id and only accept a strictly newer version, so duplicate
/// or out-of-order deliveries are harmless. The upsert is EF: a version-guarded ExecuteUpdate, then an insert when the
/// row is new (a concurrent insert loses on the primary key and is ignored). The ERP order id is resolved to a CRM
/// quote; if the quote has not been marked Ordered yet the call throws not-found and the activity retries. Return
/// requests are CRM-owned: written together with a ReturnRequested outbox message; the ERP decision comes back via
/// RecordReturnDecision.
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
        var now = DateTime.UtcNow;
        string carrier = i.Carrier ?? "", tracking = i.TrackingNumber ?? "";
        var (tenantId, erpId, status, version) = (i.TenantId, i.ErpShipmentId, i.Status, i.Version);
        var set = Db.Set<Shipment>();
        Task<int> ApplyIfNewer() => set
            .Where(s => s.TenantId == tenantId && s.ErpShipmentId == erpId && s.QuoteId == quoteId && s.Version < version)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, status).SetProperty(s => s.Carrier, carrier)
                .SetProperty(s => s.TrackingNumber, tracking).SetProperty(s => s.Version, version)
                .SetProperty(s => s.UpdatedAt, now), ct);
        if (await ApplyIfNewer() == 0 && !await set.AnyAsync(s => s.TenantId == tenantId && s.ErpShipmentId == erpId, ct) &&
            !await InsertIfAbsentAsync(new Shipment
            {
                TenantId = tenantId, ErpShipmentId = erpId, QuoteId = quoteId, ErpOrderId = i.ErpOrderId,
                Status = status, Carrier = carrier, TrackingNumber = tracking, Version = version, UpdatedAt = now
            }, ct))
            await ApplyIfNewer(); // lost the insert race to a concurrent delivery that may hold an OLDER version
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
        var now = DateTime.UtcNow;
        var issued = i.IssuedAt.ToUniversalTime();
        DateTime? due = i.DueAt?.ToUniversalTime();
        var (tenantId, erpId, number, total, status, version) = (i.TenantId, i.ErpInvoiceId, i.Number, i.Total, i.Status, i.Version);
        var set = Db.Set<Invoice>();
        Task<int> ApplyIfNewer() => set
            .Where(x => x.TenantId == tenantId && x.ErpInvoiceId == erpId && x.QuoteId == quoteId && x.Version < version)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Number, number).SetProperty(x => x.Total, total)
                .SetProperty(x => x.Status, status).SetProperty(x => x.IssuedAt, issued).SetProperty(x => x.DueAt, due)
                .SetProperty(x => x.Version, version).SetProperty(x => x.UpdatedAt, now), ct);
        if (await ApplyIfNewer() == 0 && !await set.AnyAsync(x => x.TenantId == tenantId && x.ErpInvoiceId == erpId, ct) &&
            !await InsertIfAbsentAsync(new Invoice
            {
                TenantId = tenantId, ErpInvoiceId = erpId, QuoteId = quoteId, CustomerId = q.CustomerId,
                ErpOrderId = i.ErpOrderId, Number = number, Total = total, Status = status, IssuedAt = issued,
                DueAt = due, Version = version, UpdatedAt = now
            }, ct))
            await ApplyIfNewer(); // lost the insert race to a concurrent delivery that may hold an OLDER version
    }

    public async Task RecordPaymentAsync(RecordPaymentInput i, CancellationToken ct)
    {
        Require(i.ErpPaymentId, "erpPaymentId");
        Require(i.ErpInvoiceId, "erpInvoiceId");
        RequireStatus(i.Status, PaymentStatuses.All, "payment");
        RequireVersion(i.Version);
        if (i.Amount < 0) throw new CrmValidationException("Payment amount cannot be negative.");
        var quoteId = await ResolveQuoteAsync(i.TenantId, i.ErpOrderId, ct);
        var now = DateTime.UtcNow;
        var (tenantId, erpId, amount, status, version) = (i.TenantId, i.ErpPaymentId, i.Amount, i.Status, i.Version);
        var set = Db.Set<Payment>();
        Task<int> ApplyIfNewer() => set
            .Where(x => x.TenantId == tenantId && x.ErpPaymentId == erpId && x.QuoteId == quoteId && x.Version < version)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Amount, amount).SetProperty(x => x.Status, status)
                .SetProperty(x => x.Version, version).SetProperty(x => x.UpdatedAt, now), ct);
        if (await ApplyIfNewer() == 0 && !await set.AnyAsync(x => x.TenantId == tenantId && x.ErpPaymentId == erpId, ct) &&
            !await InsertIfAbsentAsync(new Payment
            {
                TenantId = tenantId, ErpPaymentId = erpId, ErpInvoiceId = i.ErpInvoiceId, QuoteId = quoteId,
                Amount = amount, Status = status, Version = version, UpdatedAt = now
            }, ct))
            await ApplyIfNewer(); // lost the insert race to a concurrent delivery that may hold an OLDER version
    }

    // ---- Reads (current tenant) -----------------------------------------------------------------------------------

    public async Task<FulfilmentView> GetFulfilmentAsync(int quoteId, CancellationToken ct)
    {
        var q = await GetQuoteAsync(quoteId, ct);
        var t = q.TenantId;
        var shipments = await Db.Set<Shipment>().AsNoTracking()
            .Where(s => s.TenantId == t && s.QuoteId == quoteId).OrderBy(s => s.ErpShipmentId).ToListAsync(ct);
        var invoices = await Db.Set<Invoice>().AsNoTracking()
            .Where(x => x.TenantId == t && x.QuoteId == quoteId)
            .OrderByDescending(x => x.IssuedAt).ThenBy(x => x.ErpInvoiceId).ToListAsync(ct);
        var payments = await Db.Set<Payment>().AsNoTracking()
            .Where(p => p.TenantId == t && p.QuoteId == quoteId).OrderBy(p => p.ErpPaymentId).ToListAsync(ct);
        return new FulfilmentView(q.Id, q.ErpOrderId, shipments, invoices, payments);
    }

    public async Task<IReadOnlyList<Invoice>> ListInvoicesForCustomerAsync(int customerId, CancellationToken ct)
    {
        var c = await customers.GetAsync(customerId, ct);
        if (c is null || c.TenantId != tenant.TenantId) throw new CrmNotFoundException($"Customer {customerId} not found.");
        var t = c.TenantId;
        return await Db.Set<Invoice>().AsNoTracking().Where(x => x.TenantId == t && x.CustomerId == customerId)
            .OrderByDescending(x => x.IssuedAt).ThenBy(x => x.ErpInvoiceId).ToListAsync(ct);
    }

    // ---- Returns --------------------------------------------------------------------------------------------------

    public async Task<ReturnView> RequestReturnAsync(int quoteId, ReturnRequestInput r, CancellationToken ct)
    {
        var q = await GetQuoteAsync(quoteId, ct);
        if (q.Status != QuoteStatuses.Ordered) throw new CrmConflictException("Only an ordered quote can have returns.");
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500) throw new CrmValidationException("A reason (up to 500 characters) is required.");
        var wanted = MergeLines(r.Lines);
        var mapping = await integration.GetMappingAsync(q.TenantId, q.CustomerId, ct);
        if (mapping is not { Status: MappingStatuses.Active } || string.IsNullOrEmpty(mapping.ErpCustomerId))
            throw new CrmConflictException("The customer is not yet synchronised to the ERP.");

        var t = q.TenantId;
        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        // Serialise concurrent return requests for the same quote so the quantity cap below cannot be raced.
        // EF has no row-lock hint, so this is the one raw statement (UPDLOCK = SQL Server's FOR UPDATE); it still materialises the EF entity.
        await Db.Set<Quote>()
            .FromSql($"SELECT * FROM quotes WITH (UPDLOCK, ROWLOCK) WHERE quote_id = {quoteId} AND tenant_id = {t}")
            .AsNoTracking().ToListAsync(ct);
        var quoted = await Db.Set<QuoteLine>().AsNoTracking().Where(l => l.QuoteId == quoteId && l.TenantId == t)
            .GroupBy(l => l.Sku).Select(g => new { Sku = g.Key, Qty = g.Sum(l => (long)l.Quantity) })
            .ToDictionaryAsync(x => x.Sku, x => x.Qty, ct);
        var already = await (from l in Db.Set<ReturnRequestLine>().AsNoTracking()
                join rr in Db.Set<ReturnRequest>() on l.ReturnId equals rr.Id
                where rr.QuoteId == quoteId && rr.TenantId == t && rr.Status != ReturnStatuses.Rejected
                group l by l.Sku into g
                select new { Sku = g.Key, Qty = g.Sum(x => (long)x.Quantity) })
            .ToDictionaryAsync(x => x.Sku, x => x.Qty, ct);
        foreach (var (sku, qty) in wanted)
        {
            if (!quoted.TryGetValue(sku, out var ordered)) throw new CrmValidationException($"SKU {sku} is not on this quote.");
            if (already.GetValueOrDefault(sku) + qty > ordered)
                throw new CrmValidationException($"Cannot return {qty} of {sku}: only {ordered - already.GetValueOrDefault(sku)} remaining.");
        }

        var now = DateTime.UtcNow;
        var request = new ReturnRequest
        {
            TenantId = t, QuoteId = quoteId, CustomerId = q.CustomerId, Status = ReturnStatuses.Requested,
            Reason = r.Reason.Trim(), RequestedBy = tenant.UserId, CreatedAt = now, UpdatedAt = now
        };
        Db.Set<ReturnRequest>().Add(request);
        await Db.SaveChangesAsync(ct);
        Db.Set<ReturnRequestLine>().AddRange(wanted.Select(w => new ReturnRequestLine
            { TenantId = t, ReturnId = request.Id, Sku = w.Key, Quantity = w.Value }));
        await Db.SaveChangesAsync(ct);

        var messageId = Guid.NewGuid();
        var dto = new ReturnRequestedDto(1, t, request.Id, quoteId, q.CustomerId, mapping.ErpCustomerId!, q.ErpOrderId,
            messageId.ToString(), r.Reason.Trim(), wanted.Select(w => new ReturnLineDto(w.Key, w.Value)).ToArray());
        await Outbox.EnqueueAsync(Db, messageId, t, FulfilmentIntegrationNames.ReturnRequested, request.Id, 1,
            JsonSerializer.Serialize(dto), ct, tenant.UserId);
        await tx.CommitAsync(ct);
        var returnId = request.Id;
        Db.ChangeTracker.Clear();
        return await GetReturnAsync(returnId, ct);
    }

    public async Task<ReturnView> GetReturnAsync(int returnId, CancellationToken ct)
    {
        var t = tenant.TenantId;
        var row = await Db.Set<ReturnRequest>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == returnId && x.TenantId == t, ct)
                  ?? throw new CrmNotFoundException($"Return {returnId} not found.");
        return new ReturnView(row, await LinesAsync(t, [returnId], ct));
    }

    public async Task<IReadOnlyList<ReturnView>> ListReturnsForQuoteAsync(int quoteId, CancellationToken ct)
    {
        var q = await GetQuoteAsync(quoteId, ct);
        var t = q.TenantId;
        var rows = await Db.Set<ReturnRequest>().AsNoTracking().Where(x => x.TenantId == t && x.QuoteId == quoteId)
            .OrderBy(x => x.Id).ToListAsync(ct);
        var lines = await LinesAsync(t, rows.Select(x => x.Id).ToArray(), ct);
        return rows.Select(x => new ReturnView(x, lines.Where(l => l.ReturnId == x.Id).ToList())).ToList();
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
        var (tenantId, returnId, decision) = (i.TenantId, i.ReturnId, i.Decision);
        var current = await Db.Set<ReturnRequest>().AsNoTracking()
                          .FirstOrDefaultAsync(x => x.Id == returnId && x.TenantId == tenantId, ct)
                      ?? throw new CrmNotFoundException($"Return {i.ReturnId} not found.");
        if (current.Status == decision)
        {
            if (decision == ReturnStatuses.Authorized && current.ErpReturnId != i.ErpReturnId)
                throw new CrmConflictException("The return is already linked to a different ERP return.");
            return;
        }

        if (!ReturnStatuses.CanMove(current.Status, decision))
            throw new CrmConflictException($"A {current.Status} return cannot become {decision}.");
        var erpId = i.ErpReturnId ?? "";
        var note = (i.Note ?? "").Length > 500 ? i.Note![..500] : i.Note ?? "";
        var now = DateTime.UtcNow;
        var from = current.Status;
        var guarded = Db.Set<ReturnRequest>().Where(x => x.Id == returnId && x.TenantId == tenantId && x.Status == from);
        // The ERP return id is only written when one is supplied; a status-only decision keeps the stored id.
        var changed = erpId == ""
            ? await guarded.ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, decision)
                .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.DecisionNote, note), ct)
            : await guarded.ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, decision)
                .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.DecisionNote, note)
                .SetProperty(x => x.ErpReturnId, erpId), ct);
        if (changed != 1) throw new CrmConflictException("The return changed concurrently; retry.");
    }

    // ---- helpers --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Inserts the row; returns false when a concurrent delivery inserted the same key first. The winner may hold an
    /// OLDER version than <paramref name="row"/> (the relay starts one projection workflow per event and they run in
    /// parallel), so the caller must re-apply its version-guarded update instead of assuming it lost to a newer one.
    /// </summary>
    private async Task<bool> InsertIfAbsentAsync<T>(T row, CancellationToken ct) where T : class
    {
        Db.Set<T>().Add(row);
        try
        {
            await Db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return false;
        }
        finally
        {
            Db.Entry(row).State = EntityState.Detached;
        }
    }

    private async Task<int> ResolveQuoteAsync(string tenantId, string erpOrderId, CancellationToken ct)
    {
        Require(tenantId, "tenantId");
        Require(erpOrderId, "erpOrderId");
        var ids = await Db.Set<Quote>().AsNoTracking()
            .Where(q => q.TenantId == tenantId && q.ErpOrderId == erpOrderId).Select(q => q.Id).ToListAsync(ct);
        return ids.Count == 1 ? ids[0] : throw new CrmNotFoundException($"No CRM quote for ERP order {erpOrderId}.");
    }

    private async Task<Quote> GetQuoteAsync(int id, CancellationToken ct)
    {
        var q = await quotes.GetAsync(id, ct);
        return q is not null && q.TenantId == tenant.TenantId ? q : throw new CrmNotFoundException($"Quote {id} not found.");
    }

    private async Task<IReadOnlyList<ReturnRequestLine>> LinesAsync(string t, int[] returnIds, CancellationToken ct) =>
        returnIds.Length == 0
            ? []
            : await Db.Set<ReturnRequestLine>().AsNoTracking()
                .Where(l => l.TenantId == t && returnIds.Contains(l.ReturnId)).OrderBy(l => l.Id).ToListAsync(ct);

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
