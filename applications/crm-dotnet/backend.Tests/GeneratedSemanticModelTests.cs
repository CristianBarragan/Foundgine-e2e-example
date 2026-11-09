using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Crm.Api;
using Foundgine.Crm.Api.Semantics;
using Xunit;

namespace Foundgine.Crm.Semantic.Tests;

public sealed class GeneratedSemanticModelTests
{
    [Fact]
    public void Aot_generator_emits_crm_entities_fields_and_physical_storage_mappings()
    {
        Assert.Equal(3, CrmModel.Metadata.Entities.Count());

        var company = CrmModel.Metadata.Entities.Single(x => x.Name == "Company");
        Assert.Equal("crm_companies", company.EffectiveStorageName);
        Assert.Equal(6, company.EffectiveFields.Count);
        Assert.Equal("tenant_id", StorageName(company, "TenantId"));
        Assert.Equal("created_at", StorageName(company, "CreatedAt"));

        var contact = CrmModel.Metadata.Entities.Single(x => x.Name == "Contact");
        Assert.Equal("crm_contacts", contact.EffectiveStorageName);
        Assert.Equal("first_name", StorageName(contact, "FirstName"));

        var opportunity = CrmModel.Metadata.Entities.Single(x => x.Name == "Opportunity");
        Assert.Equal("crm_opportunities", opportunity.EffectiveStorageName);
        Assert.Equal("expected_close_date", StorageName(opportunity, "ExpectedCloseDate"));
    }

    [Fact]
    public void Aot_relationships_are_discovered_and_composed_into_semantic_model()
    {
        Assert.Equal(2, CrmModel.Metadata.Relationships.Count());
        Assert.Equal(3, CrmModel.Model.Entities.Count);
        Assert.True(CrmModel.Model.IsFrozen);

        var company = CrmModel.Model.Get(CrmModel.Company.Entity);
        Assert.Contains(company.Relationships, x => x.Name == "opportunities");

        var opportunity = CrmModel.Model.Get(CrmModel.Opportunity.Entity);
        Assert.Contains(opportunity.Relationships, x => x.Name == "company");
    }

    [Fact]
    public void Typed_overlay_adds_vocabulary_capabilities_and_constraints_without_replacing_ids()
    {
        var company = CrmModel.Model.Get(CrmModel.Company.Entity);
        Assert.Contains(company.EffectiveAliases, x => x.Name == "Account");
        Assert.Contains(company.Fields, x => x.Id == CrmModel.Company.Name);

        var opportunity = CrmModel.Model.Get(CrmModel.Opportunity.Entity);
        Assert.Contains(opportunity.EffectiveAliases, x => x.Name == "Deal");
        var amount = opportunity.Fields.Single(x => x.Name == "Amount");
        Assert.Contains(amount.EffectiveConstraints,
            x => x.Kind == SemanticConstraintKind.Range && x.Minimum == 0m);

        // The overlay is matched by semantic names and preserves generated structural identities.
        Assert.Equal(CrmModel.Metadata.GetEntity(CrmModel.Company.Entity).EntityId, company.Id);
    }

    [Fact]
    public void Authorization_is_tenant_conditional_and_relationship_scoped()
    {
        var policy = CrmAuthorization.Create(new CrmScope("tenant-a", true));
        var opportunities = CrmModel.Model.Get(CrmModel.Company.Entity).Relationships
            .Single(x => x.Name == "opportunities");

        Assert.NotNull(policy.GetPredicate(CrmModel.Company.Entity, AuthorizationOperation.Read));
        Assert.True(policy.GetRelationshipAccess(CrmModel.Company.Entity, opportunities.Id,
            AuthorizationOperation.Read).IsAllowed);
        Assert.False(CrmAuthorization.Create(new CrmScope("tenant-a", false))
            .GetEntityAccess(CrmModel.Opportunity.Entity, AuthorizationOperation.Write).IsAllowed);
    }

    [Fact]
    public void Sensitive_contact_fields_are_hidden_from_read_only_principals()
    {
        var readOnly = CrmAuthorization.Create(new CrmScope("tenant-a", false));
        Assert.False(readOnly.GetFieldAccess(CrmModel.Contact.Entity, CrmModel.Contact.Email,
            AuthorizationOperation.Read).IsAllowed);
        Assert.False(readOnly.GetFieldAccess(CrmModel.Contact.Entity, CrmModel.Contact.Phone,
            AuthorizationOperation.Read).IsAllowed);

        var capabilities = SemanticCapabilityContractDiscovery.Describe(CrmModel.Model, readOnly);
        var contactRead = Assert.Single(capabilities.Capabilities.Where(x => x.Id == "Contact.read"));
        Assert.DoesNotContain("Email", contactRead.Fields);
        Assert.DoesNotContain("Phone", contactRead.Fields);
        var transport = CrmSemanticCatalog.DescribeContract(CrmModel.Model, readOnly);
        var transportContactRead = Assert.Single(transport.Capabilities.Where(x => x.Id == "Contact.read"));
        Assert.DoesNotContain("Email", transportContactRead.Fields);
        Assert.DoesNotContain("Phone", transportContactRead.Fields);
    }

    [Fact]
    public void Foundgine_capability_discovery_preserves_conditional_access_without_exposing_predicates()
    {
        var contract = SemanticCapabilityContractDiscovery.Describe(
            CrmModel.Model, CrmAuthorization.Create(new CrmScope("tenant-a", true)));

        var read = Assert.Single(contract.Capabilities.Where(x => x.Id == "Company.read"));
        Assert.Equal(AuthorizationAccess.Conditional, read.Access.Access);
        Assert.NotNull(read.Access.Predicate);
        var transport = CrmSemanticCatalog.DescribeContract(
            CrmModel.Model, CrmAuthorization.Create(new CrmScope("tenant-a", true)));
        Assert.Equal("conditional", Assert.Single(transport.Capabilities.Where(x => x.Id == "Company.read")).Access);
        Assert.Contains("opportunities", read.Relationships);
        Assert.DoesNotContain("TenantId", read.Fields);
        var create = Assert.Single(transport.Capabilities.Where(x => x.Id == "Company.create"));
        Assert.Equal("allowed", create.Access);
        Assert.DoesNotContain("TenantId", create.Inputs.Select(x => x.Name));
        Assert.DoesNotContain(transport.Capabilities, x => x.Operation is "update" or "delete" or "upsert");
    }


    [Fact]
    public async Task Runtime_authorization_policy_isolated_by_async_request_scope()
    {
        var policy = new CrmRuntimeAuthorizationPolicy();

        static async Task<bool> CanWriteAsync(
            CrmRuntimeAuthorizationPolicy policy,
            CrmScope scope)
        {
            using var request = CrmAuthorizationRequestContext.Push(scope);
            await Task.Yield();
            return policy.GetEntityAccess(
                CrmModel.Company.Entity,
                AuthorizationOperation.Write).IsAllowed;
        }

        var writer = CanWriteAsync(policy, new CrmScope("tenant-writer", true));
        var reader = CanWriteAsync(policy, new CrmScope("tenant-reader", false));
        var results = await Task.WhenAll(writer, reader);

        Assert.True(results[0]);
        Assert.False(results[1]);
    }

    [Fact]
    public void Runtime_policy_denies_calls_without_a_host_request_scope_after_bootstrap()
    {
        var policy = new CrmRuntimeAuthorizationPolicy();
        policy.SealBootstrap();

        Assert.Throws<UnauthorizedAccessException>(() =>
            policy.GetEntityAccess(CrmModel.Company.Entity, AuthorizationOperation.Read));
    }

    [Fact]
    public void Runtime_policy_keeps_tenant_predicate_context_bound()
    {
        var policy = new CrmRuntimeAuthorizationPolicy();
        using var request = CrmAuthorizationRequestContext.Push(new CrmScope("tenant-runtime", true));

        var predicate = policy.GetPredicate(CrmModel.Company.Entity, AuthorizationOperation.Read);

        Assert.NotNull(predicate);
        // The tenant is a runtime execution-context binding, not a literal
        // embedded in a cached or shared provider plan.
        Assert.Equal(AuthorizationPredicateKind.Equal, predicate!.Kind);
        Assert.Equal(AuthorizationPredicateKind.ResourceParameter, predicate.Left!.Left!.Kind);
        Assert.Equal(AuthorizationPredicateKind.ContextParameter, predicate.Right!.Left!.Kind);
        Assert.Equal("context", predicate.Right!.Left!.Name);
        Assert.Null(predicate.Right!.Left!.Value);
    }

    private static string StorageName(
        Foundgine.Core.Semantic.Metadata.EntityMetadata entity, string fieldName)
    {
        var field = entity.EffectiveFields.Single(x => x.Name == fieldName);
        var column = Assert.IsType<Foundgine.Core.Semantic.Metadata.ColumnReference>(field.Column);
        return entity.Columns.Single(x => x.Id == column.ColumnId).EffectiveStorageName;
    }
}
