using Crm.Api.Domain;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Application;

public sealed record StageSummary(string Stage, int Count, decimal Amount);

public sealed record PipelineReport(IReadOnlyList<StageSummary> Stages, decimal OpenPipeline, decimal WonAmount,
    double? WinRate);

public sealed record AgingBucket(string Bucket, decimal Amount);

public sealed record ReceivableRow(int CustomerId, string CustomerName, decimal Outstanding,
    IReadOnlyList<AgingBucket> Aging);

public sealed record QuoteWithLines(Quote Quote, IReadOnlyList<QuoteLine> Lines);

public sealed record Customer360(
    Customer Customer, CustomerIntegrationMapping? Integration, IReadOnlyList<Contact> Contacts,
    IReadOnlyList<Opportunity> Opportunities, IReadOnlyList<QuoteWithLines> Quotes,
    IReadOnlyList<Shipment> Shipments, IReadOnlyList<Invoice> Invoices, IReadOnlyList<Payment> Payments,
    IReadOnlyList<ReturnRequest> Returns, decimal InvoicedTotal, decimal PaidTotal, decimal Outstanding);

/// <summary>
/// Cross-entity scenarios. These read-only reports use explicit tenant-scoped EF Core subqueries so relationship
/// predicates are translated as correlated EXISTS/COUNT queries by the relational provider. Every related-table
/// predicate includes the caller's tenant, preventing cross-tenant matches even if identifiers are ever reused.
/// </summary>
public sealed class InsightsService(FoundgineDbContext db, CustomerService customers, ITenantContext tenant)
{
    private static readonly string[] Settled = [InvoiceStatuses.Paid, InvoiceStatuses.Void];
    private static readonly string[] PaidIn = [PaymentStatuses.Received, PaymentStatuses.Reconciled];
    private static readonly string[] StageOrder =
        [.. OpportunityStages.Open, OpportunityStages.ClosedWon, OpportunityStages.ClosedLost];
    private static readonly string[] OpenStages = OpportunityStages.Open;
    private static readonly string[] BucketOrder = ["Current", "1-30", "31-60", "60+"];

    /// <summary>Active customers with an open opportunity, nothing accepted or ordered, and at least one failed deal.</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AtRiskCustomersAsync(CancellationToken ct)
    {
        var t = tenant.TenantId;
        var customers = await db.Set<Customer>().AsNoTracking()
            .Where(c => c.TenantId == t && c.Status == CustomerStatuses.Active
                && db.Set<Opportunity>().Any(o => o.TenantId == t && o.CustomerId == c.Id
                    && OpenStages.Contains(o.Stage))
                && !db.Set<Quote>().Any(q => q.TenantId == t && q.CustomerId == c.Id
                    && (q.Status == QuoteStatuses.Accepted || q.Status == QuoteStatuses.Ordered))
                && db.Set<Quote>().Any(q => q.TenantId == t && q.CustomerId == c.Id
                    && (q.Status == QuoteStatuses.Rejected || q.Status == QuoteStatuses.OrderFailed)))
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .ToListAsync(ct);
        return CustomerRows(customers);
    }

    /// <summary>Active customers with at least <paramref name="minOrders"/> quotes that became ERP orders.</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> KeyAccountsAsync(int minOrders, CancellationToken ct)
    {
        var t = tenant.TenantId;
        var required = Math.Clamp(minOrders, 1, 1000);
        var customers = await db.Set<Customer>().AsNoTracking()
            .Where(c => c.TenantId == t && c.Status == CustomerStatuses.Active
                && db.Set<Quote>().Count(q => q.TenantId == t && q.CustomerId == c.Id
                    && q.Status == QuoteStatuses.Ordered) >= required)
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .ToListAsync(ct);
        return CustomerRows(customers);
    }

    /// <summary>Customers whose ERP link is Pending or Failed, so their quotes cannot be accepted yet.</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> IntegrationAttentionAsync(CancellationToken ct)
    {
        var t = tenant.TenantId;
        var customers = await db.Set<Customer>().AsNoTracking()
            .Where(c => c.TenantId == t
                && db.Set<CustomerIntegrationMapping>().Any(m => m.TenantId == t
                    && m.CrmCustomerId == c.Id
                    && (m.Status == MappingStatuses.Failed || m.Status == MappingStatuses.Pending)))
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .ToListAsync(ct);
        return CustomerRows(customers);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> CustomerRows(IEnumerable<Customer> customers) =>
        customers.Select(c => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            [nameof(Customer.Id)] = c.Id,
            [nameof(Customer.TenantId)] = c.TenantId,
            [nameof(Customer.Kind)] = c.Kind,
            [nameof(Customer.Name)] = c.Name,
            [nameof(Customer.Email)] = c.Email,
            [nameof(Customer.Phone)] = c.Phone,
            [nameof(Customer.Status)] = c.Status,
            [nameof(Customer.CreatedAt)] = c.CreatedAt,
            [nameof(Customer.CreatedBy)] = c.CreatedBy,
            [nameof(Customer.UpdatedAt)] = c.UpdatedAt,
            [nameof(Customer.UpdatedBy)] = c.UpdatedBy
        }).ToArray();

    /// <summary>Open opportunities untouched for <paramref name="days"/> days whose quotes are equally quiet.</summary>
    public async Task<IReadOnlyList<Opportunity>> StaleOpportunitiesAsync(int days, CancellationToken ct)
    {
        var t = tenant.TenantId;
        var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 3650));
        return await db.Set<Opportunity>().AsNoTracking()
            .Where(o => o.TenantId == t && OpenStages.Contains(o.Stage) && o.UpdatedAt < cutoff &&
                        !db.Set<Quote>().Any(q => q.OpportunityId == o.Id && q.UpdatedAt >= cutoff))
            .OrderBy(o => o.UpdatedAt).Take(100)
            .ToListAsync(ct);
    }

    public async Task<PipelineReport> PipelineAsync(CancellationToken ct)
    {
        var t = tenant.TenantId;
        var groups = await db.Set<Opportunity>().AsNoTracking().Where(o => o.TenantId == t)
            .GroupBy(o => o.Stage)
            .Select(g => new { Stage = g.Key, Count = g.Count(), Amount = g.Sum(o => o.Amount) })
            .ToListAsync(ct);
        var stages = groups.Select(g => new StageSummary(g.Stage, g.Count, g.Amount))
            .OrderBy(s => Array.IndexOf(StageOrder, s.Stage))
            .ToList();
        var won = stages.FirstOrDefault(s => s.Stage == OpportunityStages.ClosedWon);
        var lost = stages.FirstOrDefault(s => s.Stage == OpportunityStages.ClosedLost);
        var closed = (won?.Count ?? 0) + (lost?.Count ?? 0);
        return new PipelineReport(stages, stages.Where(s => OpportunityStages.IsOpen(s.Stage)).Sum(s => s.Amount),
            won?.Amount ?? 0, closed == 0 ? null : (double)(won?.Count ?? 0) / closed);
    }

    /// <summary>Unpaid invoice balances per customer (invoice total minus received payments), aged by due date.</summary>
    public async Task<IReadOnlyList<ReceivableRow>> ReceivablesAsync(CancellationToken ct)
    {
        var t = tenant.TenantId;
        var now = DateTime.UtcNow;
        var open = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.TenantId == t && !Settled.Contains(i.Status)).ToListAsync(ct);
        var paid = await db.Set<Payment>().AsNoTracking()
            .Where(p => p.TenantId == t && PaidIn.Contains(p.Status))
            .GroupBy(p => p.ErpInvoiceId).Select(g => new { Invoice = g.Key, Amount = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.Invoice, x => x.Amount, ct);

        var balances = open
            .Select(i => (Invoice: i, Balance: Math.Max(0, i.Total - paid.GetValueOrDefault(i.ErpInvoiceId))))
            .Where(x => x.Balance > 0).ToList();
        var customerIds = balances.Select(x => x.Invoice.CustomerId).Distinct().ToList();
        var names = await db.Set<Customer>().AsNoTracking()
            .Where(c => c.TenantId == t && customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        static string Bucket(DateTime? due, DateTime now) => due is null || due >= now ? "Current"
            : (now - due.Value).TotalDays <= 30 ? "1-30" : (now - due.Value).TotalDays <= 60 ? "31-60" : "60+";

        return balances.GroupBy(x => x.Invoice.CustomerId)
            .Select(g => new ReceivableRow(g.Key, names.GetValueOrDefault(g.Key, ""), g.Sum(x => x.Balance),
                g.GroupBy(x => Bucket(x.Invoice.DueAt, now)).Select(b => new AgingBucket(b.Key, b.Sum(x => x.Balance)))
                    .OrderBy(b => Array.IndexOf(BucketOrder, b.Bucket)).ToArray()))
            .OrderByDescending(r => r.Outstanding).ThenBy(r => r.CustomerId).ToList();
    }

    /// <summary>Everything the CRM knows about one customer, assembled with a handful of tenant-scoped reads.</summary>
    public async Task<Customer360> Customer360Async(int customerId, CancellationToken ct)
    {
        var customer = await customers.GetAsync(customerId, ct); // tenant check
        var t = tenant.TenantId;
        var contacts = await db.Set<Contact>().AsNoTracking().Where(c => c.TenantId == t && c.CustomerId == customerId)
            .OrderBy(c => c.LastName).ThenBy(c => c.Id).ToListAsync(ct);
        var opportunities = await db.Set<Opportunity>().AsNoTracking()
            .Where(o => o.TenantId == t && o.CustomerId == customerId).OrderByDescending(o => o.Id).ToListAsync(ct);
        var quotes = await db.Set<Quote>().AsNoTracking()
            .Where(q => q.TenantId == t && q.CustomerId == customerId).OrderByDescending(q => q.Id).ToListAsync(ct);
        var quoteIds = quotes.Select(q => q.Id).ToList();
        var lines = await db.Set<QuoteLine>().AsNoTracking()
            .Where(l => l.TenantId == t && quoteIds.Contains(l.QuoteId)).OrderBy(l => l.Id).ToListAsync(ct);
        var shipments = await db.Set<Shipment>().AsNoTracking()
            .Where(s => s.TenantId == t && quoteIds.Contains(s.QuoteId)).OrderBy(s => s.ErpShipmentId).ToListAsync(ct);
        var invoices = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.TenantId == t && i.CustomerId == customerId).OrderBy(i => i.IssuedAt).ToListAsync(ct);
        var payments = await db.Set<Payment>().AsNoTracking()
            .Where(p => p.TenantId == t && quoteIds.Contains(p.QuoteId)).OrderBy(p => p.ErpPaymentId).ToListAsync(ct);
        var returns = await db.Set<ReturnRequest>().AsNoTracking()
            .Where(r => r.TenantId == t && r.CustomerId == customerId).OrderByDescending(r => r.Id).ToListAsync(ct);
        var mapping = await db.Set<CustomerIntegrationMapping>().AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == t && m.CrmCustomerId == customerId, ct);

        var invoiced = invoices.Where(i => i.Status != InvoiceStatuses.Void).Sum(i => i.Total);
        var paid = payments.Where(p => PaidIn.Contains(p.Status)).Sum(p => p.Amount);
        return new Customer360(customer, mapping, contacts, opportunities,
            quotes.Select(q => new QuoteWithLines(q, lines.Where(l => l.QuoteId == q.Id).ToArray())).ToArray(),
            shipments, invoices, payments, returns, invoiced, paid, Math.Max(0, invoiced - paid));
    }
}
