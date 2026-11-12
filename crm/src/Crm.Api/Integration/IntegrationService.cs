using Crm.Api.Application;
using Crm.Api.Domain;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Integration;

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
        var attempt = await Db.Set<CustomerIntegrationMapping>().AsNoTracking().Where(m => m.CrmCustomerId == c.Id)
            .Select(m => m.Attempt).FirstOrDefaultAsync(ct);
        return new CustomerSyncDto(1, c.TenantId, c.Id, Math.Max(attempt, 1), "", c.Kind, c.Name, c.Email, c.Phone, c.Status);
    }

    /// <summary>Idempotent: the same ERP id again is a no-op; a different ERP id for an Active mapping is a conflict.</summary>
    public async Task RecordErpCustomerAsync(RecordErpCustomerInput i, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(i.ErpCustomerId)) throw new CrmValidationException("erpCustomerId is required.");
        var m = await GetMappingAsync(i.TenantId, i.CrmCustomerId, ct)
                ?? throw new CrmNotFoundException($"No mapping for customer {i.CrmCustomerId}.");
        if (m.Status == MappingStatuses.Active)
        {
            if (m.ErpCustomerId == i.ErpCustomerId) return;
            throw new CrmConflictException($"Customer {i.CrmCustomerId} is already linked to a different ERP customer.");
        }

        var now = DateTime.UtcNow;
        var erpId = i.ErpCustomerId;
        await Db.Set<CustomerIntegrationMapping>()
            .Where(x => x.CrmCustomerId == i.CrmCustomerId && x.TenantId == i.TenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, MappingStatuses.Active)
                .SetProperty(x => x.ErpCustomerId, (string?)erpId).SetProperty(x => x.LastError, (string?)null)
                .SetProperty(x => x.UpdatedAt, now), ct);
    }

    public async Task MarkSyncFailedAsync(MarkSyncFailedInput i, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var reason = i.Reason;
        await Db.Set<CustomerIntegrationMapping>()
            .Where(x => x.CrmCustomerId == i.CrmCustomerId && x.TenantId == i.TenantId && x.Status != MappingStatuses.Active)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, MappingStatuses.Failed)
                .SetProperty(x => x.LastError, (string?)reason).SetProperty(x => x.UpdatedAt, now), ct);
    }

    /// <summary>
    /// Re-queues onboarding for this tenant's Failed mappings and Pending ones older than <paramref name="olderThan"/>
    /// by bumping the attempt (a new deterministic workflow id) and writing a new outbox message. Returns the count.
    /// </summary>
    public async Task<int> ReconcileAsync(string tenantId, TimeSpan olderThan, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var stale = await Db.Set<CustomerIntegrationMapping>().AsNoTracking()
            .Where(m => m.TenantId == tenantId &&
                        (m.Status == MappingStatuses.Failed || (m.Status == MappingStatuses.Pending && m.UpdatedAt < cutoff)))
            .OrderBy(m => m.CrmCustomerId)
            .Select(m => new { m.CrmCustomerId, m.Attempt })
            .ToListAsync(ct);

        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        foreach (var row in stale)
        {
            var c = await customers.GetAsync(row.CrmCustomerId, ct);
            if (c is null) continue;
            var attempt = row.Attempt + 1;
            var now = DateTime.UtcNow;
            var id = row.CrmCustomerId;
            await Db.Set<CustomerIntegrationMapping>().Where(m => m.CrmCustomerId == id)
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.Attempt, attempt)
                    .SetProperty(m => m.Status, MappingStatuses.Pending).SetProperty(m => m.UpdatedAt, now), ct);
            await Outbox.EnqueueOnboardingAsync(Db, c, attempt, ct);
        }

        await tx.CommitAsync(ct);
        return stale.Count;
    }

    public Task<CustomerIntegrationMapping?> GetMappingAsync(string tenantId, int crmCustomerId, CancellationToken ct) =>
        Db.Set<CustomerIntegrationMapping>().AsNoTracking()
            .FirstOrDefaultAsync(m => m.CrmCustomerId == crmCustomerId && m.TenantId == tenantId, ct);
}
