using Crm.Api.Application;
using Crm.Api.Domain;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Integration;

public sealed class MappingRow
{
    public int CrmCustomerId { get; set; }
    public string TenantId { get; set; } = "";
    public string? ErpCustomerId { get; set; }
    public string Status { get; set; } = "";
    public int Attempt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Integration use cases with an explicit tenant argument (no HTTP context): used by Temporal activities and the
/// admin reconcile endpoint. CRM and ERP identifiers are kept apart; this table is the only link.
/// </summary>
public sealed class IntegrationService(IFoundgineRepository<Customer> customers)
{
    private DbContext Db => customers.Context;

    public async Task<CustomerSyncDto> GetCustomerForSyncAsync(CustomerRef r, CancellationToken ct)
    {
        var c = await customers.GetAsync(r.CrmCustomerId, ct);
        if (c is null || c.TenantId != r.TenantId) throw new CrmNotFoundException($"Customer {r.CrmCustomerId} not found.");
        var attempt = await Db.Database.SqlQuery<int>($@"
            SELECT attempt AS ""Value"" FROM customer_integration_mappings WHERE crm_customer_id = {c.Id}").FirstOrDefaultAsync(ct);
        return new CustomerSyncDto(1, c.TenantId, c.Id, Math.Max(attempt, 1), "", c.Kind, c.Name, c.Email, c.Phone, c.Status);
    }

    /// <summary>Idempotent: the same ERP id again is a no-op; a different ERP id for an Active mapping is a conflict.</summary>
    public async Task RecordErpCustomerAsync(RecordErpCustomerInput i, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(i.ErpCustomerId)) throw new CrmValidationException("erpCustomerId is required.");
        var m = await GetMappingAsync(i.TenantId, i.CrmCustomerId, ct)
                ?? throw new CrmNotFoundException($"No mapping for customer {i.CrmCustomerId}.");
        if (m.Status == "Active")
        {
            if (m.ErpCustomerId == i.ErpCustomerId) return;
            throw new CrmConflictException($"Customer {i.CrmCustomerId} is already linked to a different ERP customer.");
        }

        await Db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE customer_integration_mappings
            SET status = 'Active', erp_customer_id = {i.ErpCustomerId}, last_error = NULL, updated_at = {DateTime.UtcNow}
            WHERE crm_customer_id = {i.CrmCustomerId} AND tenant_id = {i.TenantId}", ct);
    }

    public async Task MarkSyncFailedAsync(MarkSyncFailedInput i, CancellationToken ct)
    {
        await Db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE customer_integration_mappings
            SET status = 'Failed', last_error = {i.Reason}, updated_at = {DateTime.UtcNow}
            WHERE crm_customer_id = {i.CrmCustomerId} AND tenant_id = {i.TenantId} AND status <> 'Active'", ct);
    }

    /// <summary>
    /// Re-queues onboarding for this tenant's Failed mappings and Pending ones older than <paramref name="olderThan"/>
    /// by bumping the attempt (a new deterministic workflow id) and writing a new outbox message. Returns the count.
    /// </summary>
    public async Task<int> ReconcileAsync(string tenantId, TimeSpan olderThan, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var ids = await Db.Database.SqlQuery<int>($@"
            SELECT crm_customer_id AS ""Value"" FROM customer_integration_mappings
            WHERE tenant_id = {tenantId} AND (status = 'Failed' OR (status = 'Pending' AND updated_at < {cutoff}))
            ORDER BY crm_customer_id").ToListAsync(ct);

        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        foreach (var id in ids)
        {
            var c = await customers.GetAsync(id, ct);
            if (c is null) continue;
            var attempt = await Db.Database.SqlQuery<int>($@"
                UPDATE customer_integration_mappings SET attempt = attempt + 1, status = 'Pending', updated_at = {DateTime.UtcNow}
                WHERE crm_customer_id = {id} RETURNING attempt AS ""Value""").SingleAsync(ct);
            await Outbox.EnqueueOnboardingAsync(Db, c, attempt, ct);
        }

        await tx.CommitAsync(ct);
        return ids.Count;
    }

    public Task<MappingRow?> GetMappingAsync(string tenantId, int crmCustomerId, CancellationToken ct) =>
        Db.Database.SqlQuery<MappingRow>($@"
            SELECT crm_customer_id AS ""CrmCustomerId"", tenant_id AS ""TenantId"", erp_customer_id AS ""ErpCustomerId"",
                   status AS ""Status"", attempt AS ""Attempt"", last_error AS ""LastError""
            FROM customer_integration_mappings WHERE crm_customer_id = {crmCustomerId} AND tenant_id = {tenantId}")
            .FirstOrDefaultAsync(ct);
}
