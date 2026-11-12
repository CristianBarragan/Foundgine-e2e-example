using Crm.Api.Domain;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.Aot;
using Foundgine.Providers.EntityFrameworkCore;

namespace Crm.Api.Application;

public sealed class ContactService(
    IFoundgineRepository<Contact> contacts, CustomerService customers, FoundgineEfReader reader, ITenantContext tenant)
{
    public async Task<Contact> CreateAsync(int customerId, ContactRequest r, CancellationToken ct)
    {
        var customer = await customers.GetAsync(customerId, ct); // enforces tenant ownership
        Validate(r.FirstName, r.LastName, r.Email);
        var now = DateTime.UtcNow;
        return await contacts.AddAsync(new Contact
        {
            TenantId = customer.TenantId, CustomerId = customer.Id, FirstName = r.FirstName!.Trim(),
            LastName = r.LastName!.Trim(), Email = r.Email!.Trim(), Phone = r.Phone?.Trim() ?? "",
            IsPrimary = r.IsPrimary ?? false, CreatedAt = now, CreatedBy = tenant.UserId,
            UpdatedAt = now, UpdatedBy = tenant.UserId
        }, ct);
    }

    public async Task<Contact> GetAsync(int id, CancellationToken ct)
    {
        var c = await contacts.GetAsync(id, ct);
        return c is not null && c.TenantId == tenant.TenantId
            ? c
            : throw new CrmNotFoundException($"Contact {id} not found.");
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ListForCustomerAsync(
        int customerId, CancellationToken ct)
    {
        await customers.GetAsync(customerId, ct);
        var filter = new SemanticAndFilter([
            CrmSemantics.Contact.TenantId.Eq(tenant.TenantId),
            CrmSemantics.Contact.CustomerId.Eq(customerId)
        ]);
        var operation = new SemanticOperation(new SemanticReadNode(1, CrmSemantics.Contact.Entity,
            CrmSemantics.Contact.All, null, null, [],
            new SemanticQueryOptions(filter, [CrmSemantics.Contact.LastName.Asc(), CrmSemantics.Contact.Id.Asc()])));
        var result = await reader.ReadAsync(operation, ct);
        return result.Rows.Select(r => (IReadOnlyDictionary<string, object?>)r.Values).ToArray();
    }

    private static void Validate(string? first, string? last, string? email)
    {
        if (string.IsNullOrWhiteSpace(first) || first.Length > 100)
            throw new CrmValidationException("FirstName is required (max 100 characters).");
        if (string.IsNullOrWhiteSpace(last) || last.Length > 100)
            throw new CrmValidationException("LastName is required (max 100 characters).");
        if (!CustomerService.IsValidEmail(email)) throw new CrmValidationException("A valid email is required.");
    }
}
