using System.Text.RegularExpressions;
using Crm.Api.Domain;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.Aot;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Crm.Api.Application;

/// <summary>
/// Customer use cases. Every read and write is scoped to the caller's tenant here; a row from another tenant is
/// reported as not found so existence is never leaked. Reads go through Foundgine's semantic reader, writes
/// through the generic repository.
/// </summary>
public sealed partial class CustomerService(
    IFoundgineRepository<Customer> repo, FoundgineEfReader reader, ITenantContext tenant)
{
    public const int MaxPageSize = 100;

    public async Task<Customer> CreateAsync(CustomerRequest r, CancellationToken ct)
    {
        var kind = r.Kind ?? CustomerKinds.Business;
        Validate(kind, r.Name, r.Email, r.Status ?? CustomerStatuses.Active);
        var now = DateTime.UtcNow;
        var customer = new Customer
        {
            TenantId = tenant.TenantId, Kind = kind, Name = r.Name!.Trim(), Email = r.Email!.Trim(),
            Phone = r.Phone?.Trim() ?? "", Status = r.Status ?? CustomerStatuses.Active,
            CreatedAt = now, CreatedBy = tenant.UserId, UpdatedAt = now, UpdatedBy = tenant.UserId
        };
        try
        {
            await using var tx = await repo.Context.Database.BeginTransactionAsync(ct);
            var saved = await repo.AddAsync(customer, ct);
            await repo.Context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO customer_integration_mappings (crm_customer_id, tenant_id, status, attempt, created_at, updated_at)
                VALUES ({saved.Id}, {saved.TenantId}, 'Pending', 1, {now}, {now})", ct);
            await Integration.Outbox.EnqueueOnboardingAsync(repo.Context, saved, 1, ct);
            await tx.CommitAsync(ct);
            return saved;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new CrmConflictException("A customer with this email already exists.");
        }
    }

    public async Task<Customer> GetAsync(int id, CancellationToken ct)
    {
        var c = await repo.GetAsync(id, ct);
        return c is not null && c.TenantId == tenant.TenantId
            ? c
            : throw new CrmNotFoundException($"Customer {id} not found.");
    }

    public async Task<Customer> UpdateAsync(int id, CustomerRequest r, CancellationToken ct)
    {
        var existing = await GetAsync(id, ct);
        var kind = r.Kind ?? existing.Kind;
        var status = r.Status ?? existing.Status;
        var name = r.Name ?? existing.Name;
        var email = r.Email ?? existing.Email;
        Validate(kind, name, email, status);
        var updated = new Customer
        {
            Id = existing.Id, TenantId = existing.TenantId, Kind = kind, Name = name.Trim(), Email = email.Trim(),
            Phone = r.Phone?.Trim() ?? existing.Phone, Status = status,
            CreatedAt = existing.CreatedAt, CreatedBy = existing.CreatedBy,
            UpdatedAt = DateTime.UtcNow, UpdatedBy = tenant.UserId
        };
        try
        {
            return await repo.UpdateAsync(updated, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new CrmConflictException("A customer with this email already exists.");
        }
    }

    /// <summary>
    /// Search within the tenant. Exact filters (email, status, kind) use a Foundgine semantic read; free-text
    /// <paramref name="q"/> is a case-insensitive partial match on name/email, which the semantic filter set
    /// (Eq/Neq/In) cannot express, so it uses the repository's LINQ escape hatch with the same tenant scope.
    /// </summary>
    public async Task<PagedResult> SearchAsync(string? q, string? email, string? status, string? kind,
        int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, MaxPageSize);
        var tenantId = tenant.TenantId;

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{EscapeLike(q.Trim())}%";
            var query = repo.Query().Where(c => c.TenantId == tenantId &&
                                                (EF.Functions.ILike(c.Name, pattern) ||
                                                 EF.Functions.ILike(c.Email, pattern)));
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(c => c.Status == status);
            if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(c => c.Kind == kind);
            var rows = await query.OrderBy(c => c.Name).ThenBy(c => c.Id).Skip(skip).Take(take).ToListAsync(ct);
            return new PagedResult(rows.Select(ToDictionary).ToArray(), skip, take);
        }

        var filters = new List<SemanticFilterExpression> { CrmSemantics.Customer.TenantId.Eq(tenantId) };
        if (!string.IsNullOrWhiteSpace(email)) filters.Add(CrmSemantics.Customer.Email.Eq(email.Trim()));
        if (!string.IsNullOrWhiteSpace(status)) filters.Add(CrmSemantics.Customer.Status.Eq(status));
        if (!string.IsNullOrWhiteSpace(kind)) filters.Add(CrmSemantics.Customer.Kind.Eq(kind));

        var operation = new SemanticOperation(new SemanticReadNode(1, CrmSemantics.Customer.Entity,
            CrmSemantics.Customer.All, null, null, [],
            new SemanticQueryOptions(new SemanticAndFilter(filters),
                [CrmSemantics.Customer.Name.Asc(), CrmSemantics.Customer.Id.Asc()], take, skip)));
        var result = await reader.ReadAsync(operation, ct);
        return new PagedResult(result.Rows.Select(r => (IReadOnlyDictionary<string, object?>)r.Values).ToArray(),
            skip, take);
    }

    private static void Validate(string kind, string? name, string? email, string status)
    {
        if (!CustomerKinds.IsValid(kind)) throw new CrmValidationException("Kind must be Business or Individual.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new CrmValidationException("Name is required (max 200 characters).");
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || !EmailPattern().IsMatch(email.Trim()))
            throw new CrmValidationException("A valid email is required.");
        if (!CustomerStatuses.IsValid(status)) throw new CrmValidationException("Status must be Active or Inactive.");
    }

    internal static bool IsValidEmail(string? e) => !string.IsNullOrWhiteSpace(e) && e.Length <= 320 &&
                                                   EmailPattern().IsMatch(e.Trim());

    private static IReadOnlyDictionary<string, object?> ToDictionary(Customer c) => new Dictionary<string, object?>
    {
        ["Id"] = c.Id, ["TenantId"] = c.TenantId, ["Kind"] = c.Kind, ["Name"] = c.Name, ["Email"] = c.Email,
        ["Phone"] = c.Phone, ["Status"] = c.Status, ["CreatedAt"] = c.CreatedAt, ["CreatedBy"] = c.CreatedBy,
        ["UpdatedAt"] = c.UpdatedAt, ["UpdatedBy"] = c.UpdatedBy
    };

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
