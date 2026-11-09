using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Generated;
using Foundgine.Crm.Api.Domain;

namespace Foundgine.Crm.Api.Foundgine;

/// <summary>
/// Single CRM semantic source of truth. Foundgine's Roslyn generator emits the
/// structural registry from Domain/CrmDomain.cs; this class adds semantic meaning
/// and freezes the resulting contract used by authorization and planning.
/// </summary>
public static class CrmModel
{
    public static readonly MetadataRegistry Metadata = GeneratedMetadata.Build();

    public static readonly SemanticModel Model = Metadata
        .FromMetadata()
        .Overlay(CrmSemanticOverlay.Model)
        .Build()
        .Freeze();

    public static class Company
    {
        public static readonly EntityId Entity = FindEntity(nameof(CrmCompanyRecord));
        public static readonly FieldId Id = FindField(Entity, "Id");
        public static readonly FieldId TenantId = FindField(Entity, "TenantId");
        public static readonly FieldId Name = FindField(Entity, "Name");
        public static readonly FieldId Industry = FindField(Entity, "Industry");
        public static readonly FieldId Website = FindField(Entity, "Website");
        public static readonly FieldId CreatedAt = FindField(Entity, "CreatedAt");
        public static readonly FieldId[] All = [Id, TenantId, Name, Industry, Website, CreatedAt];
        public static readonly FieldId[] PublicFields = [Id, Name, Industry, Website, CreatedAt];
    }

    public static class Contact
    {
        public static readonly EntityId Entity = FindEntity(nameof(CrmContactRecord));
        public static readonly FieldId Id = FindField(Entity, "Id");
        public static readonly FieldId TenantId = FindField(Entity, "TenantId");
        public static readonly FieldId CompanyId = FindField(Entity, "CompanyId");
        public static readonly FieldId FirstName = FindField(Entity, "FirstName");
        public static readonly FieldId LastName = FindField(Entity, "LastName");
        public static readonly FieldId Email = FindField(Entity, "Email");
        public static readonly FieldId Phone = FindField(Entity, "Phone");
        public static readonly FieldId[] All = [Id, TenantId, CompanyId, FirstName, LastName, Email, Phone];
        public static readonly FieldId[] PublicFields = [Id, CompanyId, FirstName, LastName, Email, Phone];
    }

    public static class Opportunity
    {
        public static readonly EntityId Entity = FindEntity(nameof(CrmOpportunityRecord));
        public static readonly FieldId Id = FindField(Entity, "Id");
        public static readonly FieldId TenantId = FindField(Entity, "TenantId");
        public static readonly FieldId CompanyId = FindField(Entity, "CompanyId");
        public static readonly FieldId Name = FindField(Entity, "Name");
        public static readonly FieldId Amount = FindField(Entity, "Amount");
        public static readonly FieldId Stage = FindField(Entity, "Stage");
        public static readonly FieldId ExpectedCloseDate = FindField(Entity, "ExpectedCloseDate");
        public static readonly FieldId[] All = [Id, TenantId, CompanyId, Name, Amount, Stage, ExpectedCloseDate];
        public static readonly FieldId[] PublicFields = [Id, CompanyId, Name, Amount, Stage, ExpectedCloseDate];
    }

    public static bool IsCrmEntity(EntityId id) =>
        id == Company.Entity || id == Contact.Entity || id == Opportunity.Entity;

    public static bool HasRelationship(EntityId entity, RelationshipId relationship) =>
        Model.Get(entity).Relationships.Any(candidate => candidate.Id == relationship);

    public static bool IsTenantField(EntityId entity, FieldId field) =>
        IsCrmEntity(entity) && Metadata.GetEntity(entity).EffectiveFields.Any(candidate =>
            candidate.Id == field && string.Equals(candidate.Name, "TenantId", StringComparison.OrdinalIgnoreCase));

    public static bool IsSensitiveField(EntityId entity, FieldId field) =>
        IsCrmEntity(entity) && Model.Get(entity).Fields.Any(candidate =>
            candidate.Id == field && candidate.Capabilities.HasFlag(SemanticFieldCapabilities.Sensitive));

    private static EntityId FindEntity(string name)
    {
        var entity = Metadata.Entities.SingleOrDefault(
            candidate => string.Equals(
                candidate.Name,
                name,
                StringComparison.OrdinalIgnoreCase));

        return entity?.EntityId
               ?? throw new InvalidOperationException(
                   $"Entity '{name}' was not found in generated metadata. " +
                   $"Available entities: {string.Join(", ", Metadata.Entities.Select(e => e.Name))}");
    }

    private static FieldId FindField(EntityId entity, string name) => Metadata.GetEntity(entity).EffectiveFields
        .Single(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)).Id;
}
