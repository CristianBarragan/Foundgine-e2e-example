
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;
using Foundgine.Crm.Api.Foundgine;

namespace Foundgine.Crm.Api;

/// <summary>
/// ICrmStore implemented entirely over Foundgine semantic execution. There is no EF Core / direct SQL
/// path here: tenant isolation comes from the per-request policy and the executor, not from
/// "WHERE tenant_id = ..." written by hand in each method.
/// </summary>
public sealed class FoundgineCrmStore(CrmSemanticExecutor executor) : ICrmStore
{
    public async Task<IReadOnlyList<Company>> GetCompaniesAsync(
        CrmScope scope, CancellationToken ct) =>
        (await executor.ReadAsync(
            scope,
            CrmModel.Company.Entity,
            CrmModel.Company.PublicFields,
            order:
            [
                new SemanticOrderTerm(
                    CrmModel.Company.Name,
                    SemanticSortDirection.Asc)
            ],
            ct: ct))
        .Select(row => ToCompany(row, scope))
        .ToList();

    public Task<Company> CreateCompanyAsync(
        CrmScope scope, CreateCompany input, CancellationToken ct)
    {
        var row = executor.Create(
            scope,
            CrmModel.Company.Entity,
            [
                new SemanticMutationField(CrmModel.Company.Id, Guid.NewGuid()),
                new SemanticMutationField(CrmModel.Company.Name, input.Name.Trim()),
                new SemanticMutationField(CrmModel.Company.Industry, input.Industry),
                new SemanticMutationField(CrmModel.Company.Website, input.Website),
                new SemanticMutationField(CrmModel.Company.CreatedAt, DateTimeOffset.UtcNow)
            ],
            CrmModel.Company.PublicFields,
            CrmModel.Company.TenantId,
            ct);

        return Task.FromResult(ToCompany(row, scope));
    }

    public async Task<IReadOnlyList<Contact>> GetContactsAsync(
        CrmScope scope, CancellationToken ct)
    {
        var fields = scope.CanReadSensitive || scope.CanWrite
            ? CrmModel.Contact.PublicFields
            : CrmModel.Contact.PublicFields
                .Where(field =>
                    field != CrmModel.Contact.Email &&
                    field != CrmModel.Contact.Phone)
                .ToArray();

        return (await executor.ReadAsync(
            scope,
            CrmModel.Contact.Entity,
            fields,
            order:
            [
                new SemanticOrderTerm(
                    CrmModel.Contact.LastName,
                    SemanticSortDirection.Asc)
            ],
            ct: ct))
        .Select(row => ToContact(row, scope))
        .ToList();
    }

    public async Task<Contact> CreateContactAsync(
        CrmScope scope, CreateContact input, CancellationToken ct)
    {
        if (input.CompanyId is { } companyId)
            await EnsureCompanyInTenantAsync(scope, companyId, ct);

        var row = executor.Create(
            scope,
            CrmModel.Contact.Entity,
            [
                new SemanticMutationField(CrmModel.Contact.Id, Guid.NewGuid()),
                new SemanticMutationField(CrmModel.Contact.CompanyId, input.CompanyId),
                new SemanticMutationField(CrmModel.Contact.FirstName, input.FirstName.Trim()),
                new SemanticMutationField(CrmModel.Contact.LastName, input.LastName.Trim()),
                new SemanticMutationField(CrmModel.Contact.Email, input.Email),
                new SemanticMutationField(CrmModel.Contact.Phone, input.Phone)
            ],
            CrmModel.Contact.PublicFields,
            CrmModel.Contact.TenantId,
            ct);

        return ToContact(row, scope);
    }

    public async Task<IReadOnlyList<Opportunity>> GetOpportunitiesAsync(
        CrmScope scope, CancellationToken ct) =>
        (await executor.ReadAsync(
            scope,
            CrmModel.Opportunity.Entity,
            CrmModel.Opportunity.PublicFields,
            order:
            [
                new SemanticOrderTerm(
                    CrmModel.Opportunity.Amount,
                    SemanticSortDirection.Desc)
            ],
            ct: ct))
        .Select(row => ToOpportunity(row, scope))
        .ToList();

    public async Task<Opportunity> CreateOpportunityAsync(
        CrmScope scope, CreateOpportunity input, CancellationToken ct)
    {
        await EnsureCompanyInTenantAsync(scope, input.CompanyId, ct);

        var row = executor.Create(
            scope,
            CrmModel.Opportunity.Entity,
            [
                new SemanticMutationField(CrmModel.Opportunity.Id, Guid.NewGuid()),
                new SemanticMutationField(CrmModel.Opportunity.CompanyId, input.CompanyId),
                new SemanticMutationField(CrmModel.Opportunity.Name, input.Name.Trim()),
                new SemanticMutationField(CrmModel.Opportunity.Amount, input.Amount),
                new SemanticMutationField(
                    CrmModel.Opportunity.Stage,
                    string.IsNullOrWhiteSpace(input.Stage)
                        ? "Qualification"
                        : input.Stage),
                new SemanticMutationField(
                    CrmModel.Opportunity.ExpectedCloseDate,
                    input.ExpectedCloseDate)
            ],
            CrmModel.Opportunity.PublicFields,
            CrmModel.Opportunity.TenantId,
            ct);

        return ToOpportunity(row, scope);
    }

    // A foreign key must point at a company in the caller's own tenant.
    // The read goes through the same tenant predicate as every other read,
    // so another tenant's company is simply "not found".
    private async Task EnsureCompanyInTenantAsync(
        CrmScope scope, Guid companyId, CancellationToken ct)
    {
        var found = await executor.ReadAsync(
            scope,
            CrmModel.Company.Entity,
            [CrmModel.Company.Id],
            new SemanticFieldFilter(
                CrmModel.Company.Id,
                SemanticFilterOperator.Eq,
                companyId),
            ct: ct);

        if (found.Count == 0)
            throw new CrmValidationException(
                "Company does not exist in this tenant.");
    }

    private static Company ToCompany(CrmRow r, CrmScope scope) => new()
    {
        Id = r.Get<Guid>(CrmModel.Company.Id),
        TenantId = scope.TenantId,
        Name = r.Get<string>(CrmModel.Company.Name),
        Industry = r.Get<string?>(CrmModel.Company.Industry),
        Website = r.Get<string?>(CrmModel.Company.Website),
        CreatedAt = r.Get<DateTimeOffset>(CrmModel.Company.CreatedAt)
    };

    private static Contact ToContact(CrmRow r, CrmScope scope) => new()
    {
        Id = r.Get<Guid>(CrmModel.Contact.Id),
        TenantId = scope.TenantId,
        CompanyId = r.Get<Guid?>(CrmModel.Contact.CompanyId),
        FirstName = r.Get<string>(CrmModel.Contact.FirstName),
        LastName = r.Get<string>(CrmModel.Contact.LastName),
        Email = r.Get<string?>(CrmModel.Contact.Email),
        Phone = r.Get<string?>(CrmModel.Contact.Phone)
    };

    private static Opportunity ToOpportunity(CrmRow r, CrmScope scope) => new()
    {
        Id = r.Get<Guid>(CrmModel.Opportunity.Id),
        TenantId = scope.TenantId,
        CompanyId = r.Get<Guid>(CrmModel.Opportunity.CompanyId),
        Name = r.Get<string>(CrmModel.Opportunity.Name),
        Amount = r.Get<decimal>(CrmModel.Opportunity.Amount),
        Stage = r.Get<string>(CrmModel.Opportunity.Stage),
        ExpectedCloseDate =
            r.Get<DateTimeOffset?>(CrmModel.Opportunity.ExpectedCloseDate)
    };
}
