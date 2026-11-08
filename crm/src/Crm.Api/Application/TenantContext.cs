using System.Security.Claims;

namespace Crm.Api.Application;

/// <summary>Who is calling and for which tenant. Populated from the authenticated principal (claim "tenant_id").</summary>
public interface ITenantContext
{
    string TenantId { get; }
    string UserId { get; }
}

public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public const string TenantClaim = "tenant_id";

    private ClaimsPrincipal User => accessor.HttpContext?.User ?? throw new UnauthorizedAccessException();

    public string TenantId => User.FindFirstValue(TenantClaim) is { Length: > 0 } t
        ? t
        : throw new UnauthorizedAccessException("Token has no tenant.");

    public string UserId => (User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)) is { Length: > 0 } u
        ? u
        : throw new UnauthorizedAccessException("Token has no subject.");
}
