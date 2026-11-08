using Foundgine.Extensions.Temporal.Contracts;

namespace Crm.Api.Integration;

/// <summary>
/// Foundgine process definitions. Compiled into both the API (which starts processes by name) and Crm.Worker
/// (which executes the operations); the API can never submit operations, only pick a name.
/// </summary>
public static class TenantUserProvisioning
{
    public const string Name = "tenant-user-provisioning";

    /// <summary>
    /// Create the tenant's Keycloak group, then a user in it; each step is compensated if a later one fails.
    /// Payload: { "username", "email", "firstName", "lastName", "roles": ["crm-sales"], "requiredActions": ["UPDATE_PASSWORD"] }.
    /// The tenant is the caller's, taken from the validated token, never from the payload.
    /// </summary>
    public static FoundgineProcessDefinition Definition { get; } = new(Name,
    [
        new("group", "keycloak.create-tenant-group", CompensationOperation: "keycloak.delete-tenant-group"),
        new("user", "keycloak.create-user", CompensationOperation: "keycloak.delete-user")
    ]);
}
