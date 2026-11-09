using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using D = Foundgine.Crm.Api.Domain;

namespace Foundgine.Crm.Api.Foundgine;

/// <summary>
/// Small, strongly typed semantic enrichment over generated structural metadata.
/// Storage identities, scalar fields and direct relationships are generated from
/// the domain records; this overlay contributes business vocabulary and field intent.
/// </summary>
public static class CrmSemanticOverlay
{
    public static SemanticModel Model { get; } = Build();

    private static SemanticModel Build() => new SemanticModelBuilder()
        .Entity<D.CrmCompanyRecord>(EntityId.Create("Company"), "Company", entity => entity
            .Aliases("Account", "Customer account")
            .Identity(x => x.Id)
            .Field(x => x.Name, capabilities: SemanticFieldCapabilities.Default | SemanticFieldCapabilities.Writable)
            .Field(x => x.Industry, capabilities: SemanticFieldCapabilities.Default | SemanticFieldCapabilities.Writable)
            .Field(x => x.Website, capabilities: SemanticFieldCapabilities.Default | SemanticFieldCapabilities.Writable)
            .Field(x => x.TenantId, capabilities: SemanticFieldCapabilities.None)
            .FieldAlias(x => x.Name, "AccountName"))
        .Entity<D.CrmContactRecord>(EntityId.Create("Contact"), "Contact", entity => entity
            .Aliases("Person", "Lead contact")
            .Identity(x => x.Id)
            .Field(x => x.FirstName, capabilities: SemanticFieldCapabilities.Default | SemanticFieldCapabilities.Writable)
            .Field(x => x.LastName, capabilities: SemanticFieldCapabilities.Default | SemanticFieldCapabilities.Writable)
            .Field(x => x.Email, capabilities: SemanticFieldCapabilities.Default | SemanticFieldCapabilities.Writable | SemanticFieldCapabilities.Sensitive)
            .Field(x => x.Phone, capabilities: SemanticFieldCapabilities.Default | SemanticFieldCapabilities.Writable | SemanticFieldCapabilities.Sensitive)
            .Field(x => x.TenantId, capabilities: SemanticFieldCapabilities.None))
        .Entity<D.CrmOpportunityRecord>(EntityId.Create("Opportunity"), "Opportunity", entity => entity
            .Aliases("Deal", "Sales opportunity")
            .Identity(x => x.Id)
            .Field(x => x.Name, capabilities: SemanticFieldCapabilities.Default | SemanticFieldCapabilities.Writable)
            .Field(x => x.Amount, capabilities: SemanticFieldCapabilities.Default | SemanticFieldCapabilities.Writable)
            .Constraint(x => x.Amount, SemanticConstraint.Range(minimum: 0m))
            .Field(x => x.Stage, capabilities: SemanticFieldCapabilities.Default | SemanticFieldCapabilities.Writable)
            .FieldAlias(x => x.Stage, "PipelineStage")
            .Field(x => x.ExpectedCloseDate, capabilities: SemanticFieldCapabilities.Default | SemanticFieldCapabilities.Writable)
            .Field(x => x.TenantId, capabilities: SemanticFieldCapabilities.None))
        .Build();
}
