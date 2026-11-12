using System.Security.Claims;
using System.Text.Encodings.Web;
using Crm.Api.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Crm.Api.Infrastructure;

/// <summary>
/// Development/test-only authentication from headers (X-User-Id, X-Tenant-Id, X-Roles). Enabled solely by
/// configuration "Auth:DevHeaders=true". Phase 2 replaces it with Keycloak JWT bearer validation.
/// </summary>
public sealed class DevHeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "DevHeaders";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var h = Request.Headers;
        if (!h.TryGetValue("X-User-Id", out var user) || !h.TryGetValue("X-Tenant-Id", out var tenant))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.ToString()),
            new(HttpTenantContext.TenantClaim, tenant.ToString())
        };
        foreach (var role in h["X-Roles"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            claims.Add(new Claim(ClaimTypes.Role, role));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
    }
}
