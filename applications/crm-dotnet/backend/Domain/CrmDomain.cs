using Foundgine.Providers.Aot;

namespace Foundgine.Crm.Api.Domain;

/// <summary>
/// Persistence/domain shapes observed by Foundgine's compile-time metadata generator.
/// These are not HTTP DTOs and are never populated from untrusted request payloads.
/// Explicit identities preserve the CRM semantic contract across refactors.
/// </summary>
[FoundgineEntity("Company", StorageName = "crm_companies", Id = 1)]
public sealed record CrmCompanyRecord(
    [property: FoundgineField("Id", StorageName = "id", Id = 1, IsPrimaryKey = true)] Guid Id,
    [property: FoundgineSemanticDimension("tenant"), FoundgineField("TenantId", StorageName = "tenant_id", Id = 2, Index = true)] string TenantId,
    [property: FoundgineField("Name", StorageName = "name", Id = 3, Index = true)] string Name,
    [property: FoundgineField("Industry", StorageName = "industry", Id = 4)] string? Industry,
    [property: FoundgineField("Website", StorageName = "website", Id = 5)] string? Website,
    [property: FoundgineField("CreatedAt", StorageName = "created_at", Id = 6)] DateTimeOffset CreatedAt)
{
    [FoundgineRelationship(typeof(CrmOpportunityRecord), "CompanyId", "Id", Name = "opportunities")]
    public IReadOnlyList<CrmOpportunityRecord> Opportunities { get; init; } = [];
}

[FoundgineEntity("Contact", StorageName = "crm_contacts", Id = 2)]
public sealed record CrmContactRecord(
    [property: FoundgineField("Id", StorageName = "id", Id = 1, IsPrimaryKey = true)] Guid Id,
    [property: FoundgineSemanticDimension("tenant"), FoundgineField("TenantId", StorageName = "tenant_id", Id = 2, Index = true)] string TenantId,
    [property: FoundgineField("CompanyId", StorageName = "company_id", Id = 3, Index = true)] Guid? CompanyId,
    [property: FoundgineField("FirstName", StorageName = "first_name", Id = 4)] string FirstName,
    [property: FoundgineField("LastName", StorageName = "last_name", Id = 5)] string LastName,
    [property: FoundgineField("Email", StorageName = "email", Id = 6)] string? Email,
    [property: FoundgineField("Phone", StorageName = "phone", Id = 7)] string? Phone);

[FoundgineEntity("Opportunity", StorageName = "crm_opportunities", Id = 3)]
public sealed record CrmOpportunityRecord(
    [property: FoundgineField("Id", StorageName = "id", Id = 1, IsPrimaryKey = true)] Guid Id,
    [property: FoundgineSemanticDimension("tenant"), FoundgineField("TenantId", StorageName = "tenant_id", Id = 2, Index = true)] string TenantId,
    [property: FoundgineSemanticDimension("company"), FoundgineField("CompanyId", StorageName = "company_id", Id = 3, Index = true)] Guid CompanyId,
    [property: FoundgineField("Name", StorageName = "name", Id = 4)] string Name,
    [property: FoundgineField("Amount", StorageName = "amount", Id = 5)] decimal Amount,
    [property: FoundgineSemanticDimension("pipeline_stage"), FoundgineField("Stage", StorageName = "stage", Id = 6, Index = true)] string Stage,
    [property: FoundgineField("ExpectedCloseDate", StorageName = "expected_close_date", Id = 7)] DateTimeOffset? ExpectedCloseDate)
{
    [FoundgineRelationship(typeof(CrmCompanyRecord), "CompanyId", "Id", Name = "company")]
    public CrmCompanyRecord Company { get; init; } = null!;
}
