using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Extensions.Keycloak.Configuration;

namespace Foundgine.Crm.Api.Foundgine;

/// <summary>
/// CRM policy for <c>Foundgine.Extensions</c> Keycloak: which Keycloak roles may read which entities,
/// and which fields each warrant may expose. Replaces the hand-written CrmWarrantAuthority.
/// Tenant, subject and roles come only from the validated token; nothing here is caller-supplied.
/// </summary>
public static class CrmKeycloak
{
    public const string Issuer = "foundgine-crm-host";
    public const string Audience = "foundgine-crm";
    public const string KeyId = "crm-runtime-ephemeral-1";
    public static readonly TimeSpan WarrantLifetime = TimeSpan.FromMinutes(2);
    public const long MaxResults = 200;

    public static void Apply(FoundgineKeycloakOptions options, SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(model);
        model.EnsureFrozen();

        options.WarrantIssuer = Issuer;
        options.WarrantAudience = Audience;
        options.WarrantKeyId = KeyId;
        options.WarrantLifetime = WarrantLifetime;
        options.IncludeRealmRoles = true;
        options.IncludeClientRoles = false;

        // Empty ResourceScopes = "any scope within the tenant"; tenant is pinned by the constraints below.
        options.RoleGrants.Clear();
        foreach (var role in CrmScope.ReadRoles)
            options.RoleGrants[role] = model.Entities
                .Select(entity => new FoundgineKeycloakGrant
                {
                    Capability = $"{entity.Name}.read",
                    Operation = "read"
                })
                .ToList();

        options.ConstraintsFactory = (tenant, roles) =>
        {
            var canReadSensitive = roles.Any(role => CrmScope.SensitiveReadRoles.Contains(role, StringComparer.Ordinal));
            return new SecurityWarrantConstraints(
                allowedTenants: [tenant],
                allowedFields: AllowedFields(model, canReadSensitive),
                resourceScopes: [tenant],
                allowedOperations: ["read"],
                maxResults: MaxResults);
        };
        options.ResourceScopeResolver = (tenant, _) => tenant;
    }

    public static string[] AllowedFields(SemanticModel model, bool canReadSensitive) =>
        model.Entities
            .SelectMany(entity => entity.Fields
                .Where(field => !CrmModel.IsTenantField(entity.Id, field.Id))
                .Where(field => canReadSensitive || !CrmModel.IsSensitiveField(entity.Id, field.Id))
                .Select(field => field.Name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
