using System.Text.Json;
using Crm.Api.Domain;
using Crm.Api.Integration;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.Aot;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Crm.Api.Application;

/// <summary>
/// Quote lifecycle: Draft -> (submit) PendingApproval|Approved -> (approve/reject by another admin) -> (accept) Accepted
/// -> (ERP order recorded) Ordered. Every status change is a conditional UPDATE on the expected current status, so a
/// duplicate or racing request cannot apply twice. Accept also closes the opportunity and writes the outbox message
/// for QuoteToOrderWorkflow in the same transaction.
/// </summary>
public sealed class QuoteService(
    IFoundgineRepository<Quote> quotes, IFoundgineRepository<QuoteLine> lines, OpportunityService opportunities,
    IntegrationService integration, FoundgineEfReader reader, ITenantContext tenant, IOptions<QuoteOptions> options)
{
    private DbContext Db => quotes.Context;

    public async Task<QuoteView> CreateAsync(int opportunityId, QuoteRequest r, CancellationToken ct)
    {
        var opp = await opportunities.GetAsync(opportunityId, ct);
        if (!OpportunityStages.IsOpen(opp.Stage)) throw new CrmConflictException("The opportunity is closed.");
        var items = Validate(r.Lines);
        var now = DateTime.UtcNow;
        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        var q = await quotes.AddAsync(new Quote
        {
            TenantId = opp.TenantId, OpportunityId = opp.Id, CustomerId = opp.CustomerId, Status = QuoteStatuses.Draft,
            Total = Total(items), CreatedAt = now, CreatedBy = tenant.UserId, UpdatedAt = now, UpdatedBy = tenant.UserId
        }, ct);
        var saved = await AddLinesAsync(q, items, ct);
        await tx.CommitAsync(ct);
        return new QuoteView(q, saved);
    }

    public async Task<QuoteView> GetAsync(int id, CancellationToken ct)
    {
        var q = await GetQuoteAsync(id, ct);
        var tenantId = tenant.TenantId;
        var ls = await lines.ListAsync(l => l.QuoteId == id && l.TenantId == tenantId, ct);
        return new QuoteView(q, ls.OrderBy(l => l.Id).ToList());
    }

    public async Task<QuoteView> ReplaceLinesAsync(int id, QuoteRequest r, CancellationToken ct)
    {
        var q = await GetQuoteAsync(id, ct);
        if (q.Status != QuoteStatuses.Draft) throw new CrmConflictException("Only a draft quote can be edited.");
        var items = Validate(r.Lines);
        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        var changed = await Db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE quotes SET total = {Total(items)}, updated_at = {DateTime.UtcNow}, updated_by = {tenant.UserId}
            WHERE quote_id = {id} AND tenant_id = {q.TenantId} AND status = 'Draft'", ct);
        if (changed != 1) throw new CrmConflictException("The quote is no longer a draft.");
        await Db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM quote_lines WHERE quote_id = {id} AND tenant_id = {q.TenantId}", ct);
        await AddLinesAsync(q, items, ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<QuoteView> SubmitAsync(int id, CancellationToken ct)
    {
        var q = await GetQuoteAsync(id, ct);
        if (q.Total <= 0) throw new CrmValidationException("A quote needs at least one priced line.");
        var needsApproval = q.Total > options.Value.ApprovalThreshold;
        var next = needsApproval ? QuoteStatuses.PendingApproval : QuoteStatuses.Approved;
        var decidedBy = needsApproval ? "" : "auto";
        var changed = await Db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE quotes SET status = {next}, submitted_by = {tenant.UserId}, decided_by = {decidedBy},
                updated_at = {DateTime.UtcNow}, updated_by = {tenant.UserId}
            WHERE quote_id = {id} AND tenant_id = {q.TenantId} AND status = 'Draft'", ct);
        if (changed != 1) throw new CrmConflictException("Only a draft quote can be submitted.");
        return await GetAsync(id, ct);
    }

    public Task<QuoteView> ApproveAsync(int id, CancellationToken ct) => DecideAsync(id, QuoteStatuses.Approved, ct);
    public Task<QuoteView> RejectAsync(int id, CancellationToken ct) => DecideAsync(id, QuoteStatuses.Rejected, ct);

    private async Task<QuoteView> DecideAsync(int id, string outcome, CancellationToken ct)
    {
        var q = await GetQuoteAsync(id, ct);
        if (q.Status == QuoteStatuses.PendingApproval && q.SubmittedBy == tenant.UserId)
            throw new CrmForbiddenException("The submitter cannot decide their own quote.");
        var changed = await Db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE quotes SET status = {outcome}, decided_by = {tenant.UserId},
                updated_at = {DateTime.UtcNow}, updated_by = {tenant.UserId}
            WHERE quote_id = {id} AND tenant_id = {q.TenantId} AND status = 'PendingApproval'
              AND submitted_by <> {tenant.UserId}", ct);
        if (changed != 1) throw new CrmConflictException("The quote is not awaiting approval.");
        return await GetAsync(id, ct);
    }

    /// <summary>Accepting is single-shot: exactly one Approved -> Accepted transition, one outbox message.</summary>
    public async Task<QuoteView> AcceptAsync(int id, CancellationToken ct)
    {
        var view = await GetAsync(id, ct);
        var q = view.Quote;
        var mapping = await integration.GetMappingAsync(q.TenantId, q.CustomerId, ct);
        if (mapping is not { Status: "Active" } || string.IsNullOrEmpty(mapping.ErpCustomerId))
            throw new CrmConflictException("The customer is not yet synchronised to the ERP.");

        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        var changed = await Db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE quotes SET status = 'Accepted', updated_at = {DateTime.UtcNow}, updated_by = {tenant.UserId}
            WHERE quote_id = {id} AND tenant_id = {q.TenantId} AND status = 'Approved'", ct);
        if (changed != 1) throw new CrmConflictException("Only an approved quote can be accepted.");
        await Db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE opportunities SET stage = 'ClosedWon', amount = {q.Total}, updated_at = {DateTime.UtcNow}, updated_by = {tenant.UserId}
            WHERE opportunity_id = {q.OpportunityId} AND tenant_id = {q.TenantId} AND stage NOT IN ('ClosedWon','ClosedLost')", ct);

        var messageId = Guid.NewGuid();
        var dto = new QuoteAcceptedDto(1, q.TenantId, q.Id, q.OpportunityId, q.CustomerId, mapping.ErpCustomerId!,
            messageId.ToString(), q.Total,
            view.Lines.Select(l => new QuoteLineDto(l.Sku, l.Description, l.Quantity, l.UnitPrice)).ToArray());
        var payload = JsonSerializer.Serialize(dto);
        await Db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO outbox_messages (id, tenant_id, type, aggregate_id, attempt, payload, created_at)
            VALUES ({messageId}, {q.TenantId}, {QuoteIntegrationNames.QuoteAccepted}, {q.Id}, 1, {payload}::jsonb, {DateTime.UtcNow})", ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Called by the QuoteToOrderWorkflow. Idempotent for the same order id; a different one is a conflict.</summary>
    public async Task RecordErpOrderAsync(RecordErpOrderInput i, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(i.ErpOrderId)) throw new CrmValidationException("erpOrderId is required.");
        var q = await quotes.GetAsync(i.QuoteId, ct);
        if (q is null || q.TenantId != i.TenantId) throw new CrmNotFoundException($"Quote {i.QuoteId} not found.");
        if (q.Status == QuoteStatuses.Ordered)
        {
            if (q.ErpOrderId == i.ErpOrderId) return;
            throw new CrmConflictException("The quote already has a different ERP order.");
        }

        var changed = await Db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE quotes SET status = 'Ordered', erp_order_id = {i.ErpOrderId}, updated_at = {DateTime.UtcNow}
            WHERE quote_id = {i.QuoteId} AND tenant_id = {i.TenantId} AND status = 'Accepted'", ct);
        if (changed != 1) throw new CrmConflictException("The quote is not in the Accepted state.");
    }

    /// <summary>
    /// Called by the QuoteToOrderWorkflow when the ERP refused the order for good. Accepted -> OrderFailed with the reason.
    /// Idempotent (already OrderFailed is a no-op); an Ordered quote is a conflict because an ERP order exists.
    /// </summary>
    public async Task MarkOrderFailedAsync(MarkOrderFailedInput i, CancellationToken ct)
    {
        var q = await quotes.GetAsync(i.QuoteId, ct);
        if (q is null || q.TenantId != i.TenantId) throw new CrmNotFoundException($"Quote {i.QuoteId} not found.");
        if (q.Status == QuoteStatuses.OrderFailed) return;
        if (q.Status == QuoteStatuses.Ordered) throw new CrmConflictException("The quote already has an ERP order.");

        var reason = (i.Reason ?? "").Trim();
        if (reason.Length > 500) reason = reason[..500];
        var changed = await Db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE quotes SET status = 'OrderFailed', order_failure_reason = {reason}, updated_at = {DateTime.UtcNow}
            WHERE quote_id = {i.QuoteId} AND tenant_id = {i.TenantId} AND status = 'Accepted'", ct);
        if (changed != 1) throw new CrmConflictException("The quote is not in the Accepted state.");
    }

    /// <summary>Customer sales history: accepted and ordered quotes, read through Foundgine.</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SalesHistoryAsync(int customerId, CancellationToken ct)
    {
        var customerOk = await opportunities.ListForCustomerAsync(customerId, ct); // tenant check on the customer
        _ = customerOk;
        var filter = new SemanticAndFilter([
            CrmSemantics.Quote.TenantId.Eq(tenant.TenantId),
            CrmSemantics.Quote.CustomerId.Eq(customerId),
            CrmSemantics.Quote.Status.In(QuoteStatuses.Accepted, QuoteStatuses.Ordered)
        ]);
        var op = new SemanticOperation(new SemanticReadNode(1, CrmSemantics.Quote.Entity, CrmSemantics.Quote.All,
            null, null, [], new SemanticQueryOptions(filter, [CrmSemantics.Quote.Id.Desc()])));
        var result = await reader.ReadAsync(op, ct);
        return result.Rows.Select(r => (IReadOnlyDictionary<string, object?>)r.Values).ToArray();
    }

    private async Task<Quote> GetQuoteAsync(int id, CancellationToken ct)
    {
        var q = await quotes.GetAsync(id, ct);
        return q is not null && q.TenantId == tenant.TenantId ? q : throw new CrmNotFoundException($"Quote {id} not found.");
    }

    private async Task<IReadOnlyList<QuoteLine>> AddLinesAsync(Quote q, IReadOnlyList<QuoteLineRequest> items, CancellationToken ct)
    {
        var saved = new List<QuoteLine>();
        foreach (var i in items)
            saved.Add(await lines.AddAsync(new QuoteLine
            {
                TenantId = q.TenantId, QuoteId = q.Id, Sku = i.Sku!.Trim(), Description = i.Description?.Trim() ?? "",
                Quantity = i.Quantity!.Value, UnitPrice = i.UnitPrice!.Value
            }, ct));
        return saved;
    }

    private static decimal Total(IReadOnlyList<QuoteLineRequest> items) => items.Sum(i => i.Quantity!.Value * i.UnitPrice!.Value);

    private static IReadOnlyList<QuoteLineRequest> Validate(IReadOnlyList<QuoteLineRequest>? items)
    {
        if (items is null || items.Count is 0 or > 100) throw new CrmValidationException("A quote needs 1 to 100 lines.");
        foreach (var i in items)
        {
            if (string.IsNullOrWhiteSpace(i.Sku) || i.Sku.Length > 64) throw new CrmValidationException("Each line needs a SKU.");
            if (i.Quantity is null or <= 0) throw new CrmValidationException("Quantity must be positive.");
            if (i.UnitPrice is null or < 0) throw new CrmValidationException("UnitPrice cannot be negative.");
        }

        return items;
    }
}
