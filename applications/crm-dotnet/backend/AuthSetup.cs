using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Crm.Api.Foundgine;
using Foundgine.Extensions.Keycloak.Configuration;
using Foundgine.Extensions.Keycloak.Hosting;
using Foundgine.Extensions.Keycloak.Security;
using Foundgine.Extensions.Keycloak.Warrants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Foundgine.Crm.Api;

/// <summary>What Program.cs needs from the Keycloak integration.</summary>
public sealed record CrmKeycloakWiring(
    FoundgineKeycloakOptions Options,
    ISecurityWarrantKeyResolver KeyResolver,
    ISecurityExecutionContextProvider SecurityContextProvider);

public static class AuthSetup
{
    /// <summary>
    /// Keycloak JWT validation, realm roles -> role claims, and the Keycloak-backed warrant issuer, all from
    /// Foundgine.Extensions. Reads the existing "Keycloak" section (Authority, MetadataAddress, Audience,
    /// RequireHttpsMetadata); ValidIssuer is still honoured.
    /// </summary>
    public static CrmKeycloakWiring AddCrmKeycloak(this WebApplicationBuilder builder, IHttpContextAccessor accessor)
    {
        var section = builder.Configuration.GetSection("Keycloak");
        void Apply(FoundgineKeycloakOptions o)
        {
            section.Bind(o);
            CrmKeycloak.Apply(o, CrmModel.Model);
        }

        // One signing key shared by issuer and verifier. Without a configured PEM it is per-process
        // (previous behaviour). Multi-replica deployments must set Keycloak:WarrantPrivateKeyPemPath or a KMS provider.
        var probe = new FoundgineKeycloakOptions();
        Apply(probe);
        IFoundgineWarrantSigningKeyProvider signingKey = ResolveSigningKey(probe);
        builder.Services.AddSingleton(signingKey);

        var keycloak = builder.Services.AddFoundgineKeycloak(Apply);

        var resolver = FoundgineWarrantKeyResolver.Create(keycloak.Options, signingKey);
        var issuer = new FoundgineKeycloakWarrantIssuer(keycloak.Options, signingKey);
        var provider = new KeycloakSecurityExecutionContextProvider(accessor, issuer, keycloak.Options);
        builder.Services.Replace(ServiceDescriptor.Singleton<ISecurityWarrantKeyResolver>(resolver));
        builder.Services.Replace(ServiceDescriptor.Singleton<IFoundgineWarrantIssuer>(issuer));
        builder.Services.Replace(ServiceDescriptor.Singleton<ISecurityExecutionContextProvider>(provider));

        // Behind Docker the API fetches metadata from http://keycloak:8080 while browsers get tokens from
        // http://localhost:8080, so keep the explicit ValidIssuer the CRM already had.
        var validIssuer = builder.Configuration["Keycloak:ValidIssuer"];
        if (!string.IsNullOrWhiteSpace(validIssuer))
            builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                jwt => jwt.TokenValidationParameters.ValidIssuer = validIssuer);

        return new CrmKeycloakWiring(keycloak.Options, resolver, provider);
    }

    private static PemWarrantSigningKeyProvider ResolveSigningKey(FoundgineKeycloakOptions o)
    {
        var pem = !string.IsNullOrWhiteSpace(o.WarrantPrivateKeyPem) ? o.WarrantPrivateKeyPem
            : !string.IsNullOrWhiteSpace(o.WarrantPrivateKeyPemPath) ? File.ReadAllText(o.WarrantPrivateKeyPemPath)
            : null;
        return pem is null
            ? PemWarrantSigningKeyProvider.CreateEphemeral(o.WarrantKeyId)
            : new PemWarrantSigningKeyProvider(o.WarrantKeyId, pem);
    }

    public static void AddCrmAuthorizationPolicies(this WebApplicationBuilder builder) =>
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("crm-read", p => p.RequireAuthenticatedUser());
            options.AddPolicy("crm-write", p => p.RequireRole(CrmScope.WriteRoles));
            options.AddPolicy("crm-admin", p => p.RequireRole("crm-admin"));
            // Service identity used by the Temporal worker for /api/integration/* callbacks only.
            options.AddPolicy("crm-integration", p => p.RequireRole(CrmScope.IntegrationRole));
        });
}
