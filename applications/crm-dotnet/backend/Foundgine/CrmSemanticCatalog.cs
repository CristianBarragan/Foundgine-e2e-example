using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Capabilities;

namespace Foundgine.Crm.Api.Foundgine;

/// <summary>
/// Public, read-only description of the CRM semantic surface. This is discovery metadata, not an
/// authorization grant: every operation is still resolved and authorized by CrmSemanticExecutor.
/// Keep this catalog aligned with CrmModel and the DTO contracts.
/// </summary>
public static class CrmSemanticCatalog
{
    public const string ContractVersion = "crm.semantic.v1";

    public static SemanticCapabilityContractResponse DescribeContract(
        SemanticModel model, ISemanticAuthorizationPolicy policy) =>
        DescribeContract(SemanticCapabilityContractDiscovery.Describe(model, policy));

    public static SemanticCapabilityContractResponse DescribeContract(SemanticCapabilityContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        // The underlying model supports the general Foundgine mutation IR, but this REST starter
        // currently exposes create only. Do not advertise update/delete/upsert actions that have
        // no corresponding host endpoint yet.
        var exposed = contract.Capabilities.Where(capability =>
            capability.Operation is "read" or "create" or "traverse");
        return new SemanticCapabilityContractResponse(contract.Version, exposed.Select(capability =>
            new SemanticCapabilityResponse(
                capability.Id,
                capability.Name,
                capability.Operation,
                capability.Access.Access.ToString().ToLowerInvariant(),
                capability.HasSideEffects,
                capability.IsIdempotent,
                capability.Inputs.Where(input => !string.Equals(input.Name, "TenantId", StringComparison.OrdinalIgnoreCase))
                    .Select(input => new SemanticCapabilityInputResponse(
                        input.Name, input.Type, input.Required, input.Description)).ToArray(),
                capability.Constraints.Select(item => new SemanticCapabilityConstraintResponse(
                    item.Name, item.Description)).ToArray(),
                capability.Effects.Select(item => new SemanticCapabilityEffectResponse(
                    item.Name, item.Description)).ToArray(),
                capability.Fields.Where(field => !string.Equals(field, "TenantId", StringComparison.OrdinalIgnoreCase)).ToArray(),
                capability.Relationships,
                capability.EffectiveSecurityInvariants)).ToArray());
    }

    public static SemanticCapabilities Describe() => new(
        ContractVersion,
        "CRM",
        [
            Entity("Company", "Company accounts", CrmModel.Company.Entity, CrmModel.Company.All,
                (CrmModel.Company.Id, "Id", "Unique company identifier", true),
                (CrmModel.Company.TenantId, "TenantId", "Host-assigned tenant boundary", false),
                (CrmModel.Company.Name, "Name", "Company name", true),
                (CrmModel.Company.Industry, "Industry", "Industry classification", true),
                (CrmModel.Company.Website, "Website", "Company website", true),
                (CrmModel.Company.CreatedAt, "CreatedAt", "Creation timestamp", false)),
            Entity("Contact", "People associated with customer accounts", CrmModel.Contact.Entity, CrmModel.Contact.All,
                (CrmModel.Contact.Id, "Id", "Unique contact identifier", true),
                (CrmModel.Contact.TenantId, "TenantId", "Host-assigned tenant boundary", false),
                (CrmModel.Contact.CompanyId, "CompanyId", "Associated company", true),
                (CrmModel.Contact.FirstName, "FirstName", "Given name", true),
                (CrmModel.Contact.LastName, "LastName", "Family name", true),
                (CrmModel.Contact.Email, "Email", "Email address (role-restricted)", false),
                (CrmModel.Contact.Phone, "Phone", "Telephone number (role-restricted)", false)),
            Entity("Opportunity", "Sales opportunities and pipeline value", CrmModel.Opportunity.Entity, CrmModel.Opportunity.All,
                (CrmModel.Opportunity.Id, "Id", "Unique opportunity identifier", true),
                (CrmModel.Opportunity.TenantId, "TenantId", "Host-assigned tenant boundary", false),
                (CrmModel.Opportunity.CompanyId, "CompanyId", "Owning company", true),
                (CrmModel.Opportunity.Name, "Name", "Opportunity name", true),
                (CrmModel.Opportunity.Amount, "Amount", "Opportunity amount", true),
                (CrmModel.Opportunity.Stage, "Stage", "Sales pipeline stage", true),
                (CrmModel.Opportunity.ExpectedCloseDate, "ExpectedCloseDate", "Expected close date", true))
        ],
        ["read", "create"],
        ["TenantId is derived from the validated Keycloak token; callers cannot select or override it.",
         "All data operations are executed through Foundgine semantic authorization, planning and the SQL provider.",
         "Capability discovery is descriptive only and does not grant access."]);

    private static SemanticEntityCapability Entity(
        string name, string description, EntityId entityId, IReadOnlyList<FieldId> declared,
        params (FieldId Id, string Name, string Description, bool CallerSelectable)[] fields)
    {
        var semanticEntity = CrmModel.Model.Get(entityId);
        var matchesGeneratedModel = fields.All(field => semanticEntity.Fields.Any(candidate =>
            candidate.Id == field.Id && string.Equals(candidate.Name, field.Name, StringComparison.OrdinalIgnoreCase)));
        if (declared.Count != fields.Length || fields.Any(f => !declared.Contains(f.Id)) || !matchesGeneratedModel)
            throw new InvalidOperationException($"Semantic catalog for {name} does not match generated CrmModel metadata.");
        return new SemanticEntityCapability(name, description, fields.Select(f =>
            new SemanticFieldCapability(f.Name, f.Description, f.CallerSelectable)).ToArray());
    }
}

public sealed record SemanticCapabilities(
    string ContractVersion, string Domain, IReadOnlyList<SemanticEntityCapability> Entities,
    IReadOnlyList<string> SupportedOperations, IReadOnlyList<string> SecurityInvariants);
public sealed record SemanticEntityCapability(
    string Name, string Description, IReadOnlyList<SemanticFieldCapability> Fields);
public sealed record SemanticFieldCapability(string Name, string Description, bool CallerSelectable);

/// <summary>
/// A transport-safe projection of Foundgine's canonical capability contract.
/// Authorization predicates remain server-side; clients see only the access state.
/// </summary>
public sealed record SemanticCapabilityContractResponse(
    int Version, IReadOnlyList<SemanticCapabilityResponse> Capabilities);

public sealed record SemanticCapabilityResponse(
    string Id,
    string Name,
    string Operation,
    string Access,
    bool HasSideEffects,
    bool IsIdempotent,
    IReadOnlyList<SemanticCapabilityInputResponse> Inputs,
    IReadOnlyList<SemanticCapabilityConstraintResponse> Constraints,
    IReadOnlyList<SemanticCapabilityEffectResponse> Effects,
    IReadOnlyList<string> Fields,
    IReadOnlyList<string> Relationships,
    IReadOnlyList<string> RequiredSecurityInvariants);

public sealed record SemanticCapabilityInputResponse(string Name, string Type, bool Required, string? Description);
public sealed record SemanticCapabilityConstraintResponse(string Name, string Description);
public sealed record SemanticCapabilityEffectResponse(string Name, string Description);
