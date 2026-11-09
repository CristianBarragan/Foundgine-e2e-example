using System.Threading;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;

namespace Foundgine.Crm.Api.Foundgine;

/// <summary>
/// Async-flow-local request identity. It is populated only from the already
/// validated Keycloak ClaimsPrincipal by CrmScope.From; request input never
/// controls this value.
/// </summary>
public static class CrmAuthorizationRequestContext
{
    private static readonly AsyncLocal<CrmScope?> CurrentScope = new();

    public static CrmScope Current =>
        CurrentScope.Value ?? throw new UnauthorizedAccessException(
            "Foundgine execution requires a host-established CRM request scope.");

    public static IDisposable Push(CrmScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var previous = CurrentScope.Value;
        CurrentScope.Value = scope;
        return new Restore(previous);
    }

    private sealed class Restore(CrmScope? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            CurrentScope.Value = previous;
            _disposed = true;
        }
    }
}

/// <summary>
/// Foundgine Runtime keeps its authorization policy in the singleton engine.
/// This policy resolves the authenticated request's scope from AsyncLocal so
/// concurrent requests are isolated without rebuilding the Foundgine engine.
/// Outside a request it uses a bootstrap scope solely to construct the
/// immutable capability/security contract at application startup.
/// </summary>
public sealed class CrmRuntimeAuthorizationPolicy : ISemanticAuthorizationPolicy
{
    private static readonly CrmScope BootstrapScope = new("__foundgine_bootstrap__", true, true);
    private int _bootstrapAllowed = 1;

    /// <summary>
    /// Called immediately after the singleton Runtime read and mutation engines
    /// have been constructed. Thereafter, calls without an authenticated request
    /// scope are denied rather than receiving the bootstrap policy.
    /// </summary>
    public void SealBootstrap() => Interlocked.Exchange(ref _bootstrapAllowed, 0);

    private ConfiguredSemanticAuthorizationPolicy PolicyForCurrentScope()
    {
        CrmScope scope;
        try
        {
            scope = CrmAuthorizationRequestContext.Current;
        }
        catch (UnauthorizedAccessException)
        {
            if (Volatile.Read(ref _bootstrapAllowed) == 0)
                throw;
            scope = BootstrapScope;
        }

        return CrmAuthorization.Create(scope);
    }

    public bool CanAccessEntity(EntityId entityId) =>
        PolicyForCurrentScope().CanAccessEntity(entityId);

    public bool CanAccessField(EntityId entityId, FieldId fieldId) =>
        PolicyForCurrentScope().CanAccessField(entityId, fieldId);

    public bool CanAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) =>
        PolicyForCurrentScope().CanAccessRelationship(sourceEntityId, relationshipId);

    public bool CanWriteEntity(EntityId entityId) =>
        PolicyForCurrentScope().CanWriteEntity(entityId);

    public bool CanWriteField(EntityId entityId, FieldId fieldId) =>
        PolicyForCurrentScope().CanWriteField(entityId, fieldId);

    public bool CanWriteRelationship(EntityId sourceEntityId, RelationshipId relationshipId) =>
        PolicyForCurrentScope().CanWriteRelationship(sourceEntityId, relationshipId);

    public AuthorizationPredicate? GetPredicate(EntityId entityId, AuthorizationOperation operation) =>
        PolicyForCurrentScope().GetPredicate(entityId, operation);

    public AuthorizationDecision GetEntityAccess(EntityId entityId, AuthorizationOperation operation) =>
        PolicyForCurrentScope().GetEntityAccess(entityId, operation);

    public AuthorizationDecision GetEntityAccess(
        EntityId entityId,
        AuthorizationOperation operation,
        AuthorizationOperationName? name) =>
        PolicyForCurrentScope().GetEntityAccess(entityId, operation, name);

    public AuthorizationDecision GetFieldAccess(
        EntityId entityId,
        FieldId fieldId,
        AuthorizationOperation operation) =>
        PolicyForCurrentScope().GetFieldAccess(entityId, fieldId, operation);

    public AuthorizationDecision GetRelationshipAccess(
        EntityId sourceEntityId,
        RelationshipId relationshipId,
        AuthorizationOperation operation) =>
        PolicyForCurrentScope().GetRelationshipAccess(sourceEntityId, relationshipId, operation);
}
