using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;

namespace Foundgine.Crm.Api.Foundgine;

/// <summary>
/// Per-request semantic authorization. A new policy is built from the validated token for every
/// request; the tenant is never read from request input.
/// </summary>
public static class CrmAuthorization
{
    /// <summary>
    /// The SQL provider names a context-bound predicate value "{parameterName}.{member}". The predicate
    /// below uses parameter "context" and member "TenantId", so the ExecutionContext must carry this key
    /// or execution fails closed ("Execution context does not contain authorization value ...").
    /// </summary>
    public const string TenantContextKey = "context.TenantId";

    public static ConfiguredSemanticAuthorizationPolicy Create(CrmScope scope)
    {
        var context = new SemanticAuthorizationContext(scope.TenantId);
        var configuration = new SemanticAuthorizationConfiguration()
            // CRM entities only. Reads are tenant-scoped; writes require a write role.
            .AddEntityRule((_, entity, operation) =>
                CrmModel.IsCrmEntity(entity) && (operation == AuthorizationOperation.Read || scope.CanWrite))
            .AddFieldRule((_, entity, field, operation) =>
                CrmModel.IsCrmEntity(entity) && (operation == AuthorizationOperation.Read
                    ? !CrmModel.IsTenantField(entity, field) &&
                      (scope.CanReadSensitive || scope.CanWrite || !CrmModel.IsSensitiveField(entity, field))
                    : scope.CanWrite))
            // Only relationships emitted by the generated structural catalog are traversable.
            // Each target entity independently receives the same tenant predicate below.
            .AddRelationshipRule((_, entity, relationship, operation) =>
                operation == AuthorizationOperation.Read && CrmModel.HasRelationship(entity, relationship))
            // Row-level tenant isolation: resource.TenantId == context.TenantId, lowered into the SQL WHERE.
            .AddPredicateRule((_, entity, operation) =>
                operation == AuthorizationOperation.Read && CrmModel.IsCrmEntity(entity) ? TenantPredicate() : null);

        return new ConfiguredSemanticAuthorizationPolicy(configuration, context);
    }

    private static AuthorizationPredicate TenantPredicate() =>
        AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("context"), "TenantId"));
}
