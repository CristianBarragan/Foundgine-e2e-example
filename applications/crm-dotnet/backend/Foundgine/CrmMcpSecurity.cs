using System.Security.Cryptography;
using System.Security.Claims;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Microsoft.AspNetCore.Http;

namespace Foundgine.Crm.Api.Foundgine;

/// <summary>
/// Issues short-lived, host-owned warrants for authenticated HTTP/MCP requests.
/// The caller never supplies subject, tenant, grants, fields, or resource scope.
/// The current implementation uses an in-process RSA key because warrants are
/// minted and consumed within one request. Production multi-replica deployments
/// should replace this with a KMS-backed signer and a shared replay store.
/// </summary>
public sealed class CrmWarrantAuthority : ISecurityWarrantKeyResolver, IDisposable
{
    public const string Issuer = "foundgine-crm-host";
    public const string Audience = "foundgine-crm";
    public const string KeyId = "crm-runtime-ephemeral-1";

    private readonly RSA _signingKey = RSA.Create(2048);
    private readonly RSAParameters _publicParameters;
    private readonly ThreadLocal<RSA> _verificationKeys;
    private readonly object _signGate = new();
    private readonly SemanticModel _model;

    public CrmWarrantAuthority(SemanticModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _model.EnsureFrozen();
        _publicParameters = _signingKey.ExportParameters(false);
        _verificationKeys = new ThreadLocal<RSA>(() =>
        {
            var key = RSA.Create();
            key.ImportParameters(_publicParameters);
            return key;
        }, trackAllValues: true);
    }

    public RSA Resolve(string keyId) =>
        StringComparer.Ordinal.Equals(keyId, KeyId)
            ? _verificationKeys.Value!
            : throw new InvalidOperationException($"Unknown CRM warrant key '{keyId}'.");

    public SecurityExecutionContext Issue(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException("An authenticated principal is required.");

        var scope = CrmScope.From(principal);
        var subject = principal.FindFirst("sub")?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? throw new UnauthorizedAccessException("Token has no subject claim.");

        var grants = _model.Entities
            .Select(entity => new CapabilityGrant($"{entity.Name}.read", "read", [scope.TenantId]))
            .ToArray();

        var allowedFields = _model.Entities
            .SelectMany(entity => entity.Fields
                .Where(field => !CrmModel.IsTenantField(entity.Id, field.Id))
                .Where(field => scope.CanReadSensitive || !CrmModel.IsSensitiveField(entity.Id, field.Id))
                .Select(field => field.Name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var now = DateTimeOffset.UtcNow;
        SecurityWarrant warrant;
        lock (_signGate)
        {
            warrant = SecurityWarrantSigner.Sign(new SecurityWarrant(
            Guid.NewGuid().ToString("N"),
            Issuer,
            subject,
            Audience,
            grants,
            new SecurityWarrantConstraints(
                allowedTenants: [scope.TenantId],
                allowedFields: allowedFields,
                resourceScopes: [scope.TenantId],
                allowedOperations: ["read"],
                maxResults: 200),
            now.AddSeconds(-1),
            now.AddMinutes(2),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
            KeyId,
            null,
            []), _signingKey);
        }

        return new SecurityExecutionContext(warrant, subject, Audience, scope.TenantId, scope.TenantId);
    }

    public void Dispose()
    {
        foreach (var key in _verificationKeys.Values)
            key.Dispose();
        _verificationKeys.Dispose();
        _signingKey.Dispose();
    }
}

/// <summary>Reads the authenticated caller from ASP.NET Core; MCP arguments cannot override it.</summary>
public sealed class CrmHttpSecurityExecutionContextProvider(
    IHttpContextAccessor httpContextAccessor,
    CrmWarrantAuthority authority) : ISecurityExecutionContextProvider
{
    public SecurityExecutionContext? GetSecurityExecutionContext()
    {
        var context = httpContextAccessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated != true)
            return null;

        return authority.Issue(context.User);
    }
}
