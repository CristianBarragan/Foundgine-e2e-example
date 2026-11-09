using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Crm.Api.Foundgine;
using Foundgine.Extensions.Keycloak.Configuration;
using Foundgine.Extensions.Keycloak.Warrants;
using Xunit;

namespace Foundgine.Crm.Semantic.Tests;

public sealed class CrmMcpSecurityTests
{
    private static (IFoundgineWarrantIssuer Issuer, FoundgineKeycloakOptions Options, PemWarrantSigningKeyProvider Key) Create()
    {
        var options = new FoundgineKeycloakOptions();
        CrmKeycloak.Apply(options, CrmModel.Model);
        var key = PemWarrantSigningKeyProvider.CreateEphemeral(options.WarrantKeyId);
        return (new FoundgineKeycloakWarrantIssuer(options, key), options, key);
    }

    [Fact]
    public void Host_issues_a_signed_read_only_warrant_bound_to_token_tenant()
    {
        var (issuer, options, key) = Create();
        using var _ = key;

        var context = issuer.Issue("subject-123", "tenant-alpha", ["crm-readonly"]);

        Assert.NotNull(context);
        Assert.Equal("subject-123", context!.Subject);
        Assert.Equal("tenant-alpha", context.Tenant);
        Assert.Equal("tenant-alpha", context.ResourceScope);
        Assert.Contains(context.Warrant.Constraints.AllowedTenants, x => x == "tenant-alpha");
        Assert.Contains(context.Warrant.Constraints.AllowedOperations, x => x == "read");
        Assert.DoesNotContain(context.Warrant.Constraints.AllowedOperations, x => x != "read");
        Assert.DoesNotContain(context.Warrant.Constraints.AllowedFields, x => x == "TenantId");
        Assert.DoesNotContain(context.Warrant.Constraints.AllowedFields, x => x == "Email");
        SecurityWarrantVerifier.Verify(context.Warrant, FoundgineWarrantKeyResolver.Create(options, key),
            DateTimeOffset.UtcNow, CrmKeycloak.Issuer, CrmKeycloak.Audience);
    }

    [Fact]
    public void Sensitive_read_role_receives_sensitive_fields_but_never_tenant_field()
    {
        var (issuer, _, key) = Create();
        using var _k = key;

        var context = issuer.Issue("sales-user", "tenant-beta", ["sales-representative"]);

        Assert.NotNull(context);
        Assert.Contains(context!.Warrant.Constraints.AllowedFields, x => x == "Email");
        Assert.Contains(context.Warrant.Constraints.AllowedFields, x => x == "Phone");
        Assert.DoesNotContain(context.Warrant.Constraints.AllowedFields, x => x == "TenantId");
    }

    [Fact]
    public void Unmapped_roles_get_no_foundgine_authority()
    {
        var (issuer, _, key) = Create();
        using var _k = key;

        Assert.Null(issuer.Issue("svc", "tenant-alpha", ["orchestration-service"]));
    }

    [Fact]
    public void Every_issued_warrant_is_fresh_so_replay_protection_holds()
    {
        var (issuer, _, key) = Create();
        using var _k = key;

        var first = issuer.Issue("u", "tenant-alpha", ["crm-admin"])!;
        var second = issuer.Issue("u", "tenant-alpha", ["crm-admin"])!;

        Assert.NotEqual(first.Warrant.Id, second.Warrant.Id);
        Assert.NotEqual(first.Warrant.Nonce, second.Warrant.Nonce);
    }
}
