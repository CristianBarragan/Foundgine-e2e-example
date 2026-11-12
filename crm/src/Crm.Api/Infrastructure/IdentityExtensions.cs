using Crm.Api.Application;
using Foundgine.Extensions.Keycloak.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Crm.Api.Infrastructure;

public static class IdentityExtensions
{
    private const string Smart = "Smart";

    /// <summary>
    /// Identity via the Foundgine Keycloak provider: bearer validation (issuer, audience, signature, lifetime),
    /// realm roles projected to ASP.NET roles, tenant from <c>tenant_id</c>, and the Keycloak-backed
    /// ISecurityExecutionContextProvider (short-lived signed warrants). The dev-header scheme stays opt-in.
    /// </summary>
    public static FoundgineKeycloakBuilder AddCrmIdentity(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("Foundgine:Keycloak");
        var keycloak = builder.Services.AddFoundgineKeycloak(section);

        // Development/test only: per-process warrant key. Anywhere else a PEM key (mounted secret) is required.
        if (section.GetValue<bool>("EphemeralWarrantKey"))
        {
            if (!builder.Environment.IsDevelopment())
                throw new InvalidOperationException("Foundgine:Keycloak:EphemeralWarrantKey is for Development only.");
            keycloak.UseEphemeralWarrantKey();
        }

        // Registered after AddFoundgineKeycloak so this default scheme wins; "Bearer" is the Keycloak handler.
        var devHeaders = builder.Configuration.GetValue<bool>("Auth:DevHeaders");
        var auth = builder.Services.AddAuthentication(o => { o.DefaultScheme = Smart; o.DefaultChallengeScheme = Smart; });
        auth.AddPolicyScheme(Smart, Smart, o => o.ForwardDefaultSelector = ctx =>
            devHeaders && ctx.Request.Headers.ContainsKey("X-User-Id")
                ? DevHeaderAuthenticationHandler.Scheme
                : JwtBearerDefaults.AuthenticationScheme);
        if (devHeaders)
            auth.AddScheme<AuthenticationSchemeOptions, DevHeaderAuthenticationHandler>(DevHeaderAuthenticationHandler.Scheme, null);

        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(CrmRoles.ReadPolicy, p => p.RequireRole(CrmRoles.Read))
            .AddPolicy(CrmRoles.WritePolicy, p => p.RequireRole(CrmRoles.Write))
            .AddPolicy(CrmRoles.AdminPolicy, p => p.RequireRole(CrmRoles.Admin));
        return keycloak;
    }
}
