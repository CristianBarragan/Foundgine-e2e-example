using Crm.Api.Domain;
using Foundgine.Providers.EntityFrameworkCore;

namespace Crm.Api.Application;

public sealed class OpportunityService(
    IFoundgineRepository<Opportunity> repo, CustomerService customers, ITenantContext tenant)
{
    public async Task<Opportunity> CreateAsync(int customerId, OpportunityRequest r, CancellationToken ct)
    {
        var customer = await customers.GetAsync(customerId, ct); // tenant check
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200)
            throw new CrmValidationException("Name is required (max 200 characters).");
        if (r.Amount is < 0) throw new CrmValidationException("Amount cannot be negative.");
        var now = DateTime.UtcNow;
        return await repo.AddAsync(new Opportunity
        {
            TenantId = customer.TenantId, CustomerId = customer.Id, Name = r.Name.Trim(),
            Stage = OpportunityStages.Prospecting, Amount = r.Amount ?? 0, CreatedAt = now,
            CreatedBy = tenant.UserId, UpdatedAt = now, UpdatedBy = tenant.UserId
        }, ct);
    }

    public async Task<Opportunity> GetAsync(int id, CancellationToken ct)
    {
        var o = await repo.GetAsync(id, ct);
        return o is not null && o.TenantId == tenant.TenantId
            ? o
            : throw new CrmNotFoundException($"Opportunity {id} not found.");
    }

    public async Task<Opportunity> UpdateAsync(int id, OpportunityRequest r, CancellationToken ct)
    {
        var o = await GetAsync(id, ct);
        if (!OpportunityStages.IsOpen(o.Stage)) throw new CrmConflictException("A closed opportunity cannot be changed.");
        var stage = o.Stage;
        if (r.Stage is not null && r.Stage != o.Stage)
        {
            if (r.Stage == OpportunityStages.ClosedWon)
                throw new CrmValidationException("ClosedWon is set by accepting a quote.");
            if (!OpportunityStages.CanMove(o.Stage, r.Stage))
                throw new CrmValidationException($"Cannot move from {o.Stage} to {r.Stage}.");
            stage = r.Stage;
        }

        var name = r.Name ?? o.Name;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) throw new CrmValidationException("Name is invalid.");
        if (r.Amount is < 0) throw new CrmValidationException("Amount cannot be negative.");
        return await repo.UpdateAsync(new Opportunity
        {
            Id = o.Id, TenantId = o.TenantId, CustomerId = o.CustomerId, Name = name.Trim(), Stage = stage,
            Amount = r.Amount ?? o.Amount, CreatedAt = o.CreatedAt, CreatedBy = o.CreatedBy,
            UpdatedAt = DateTime.UtcNow, UpdatedBy = tenant.UserId
        }, ct);
    }

    public async Task<IReadOnlyList<Opportunity>> ListForCustomerAsync(int customerId, CancellationToken ct)
    {
        await customers.GetAsync(customerId, ct);
        var tenantId = tenant.TenantId;
        return await repo.ListAsync(o => o.CustomerId == customerId && o.TenantId == tenantId, ct);
    }
}
