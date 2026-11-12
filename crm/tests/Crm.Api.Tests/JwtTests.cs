using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

/// <summary>
/// JWT validation without Keycloak: the bearer handler is pointed at a local signing key (no discovery), keeping the
/// production validation rules (issuer, audience, lifetime, signature, "roles"/"tenant_id" claims).
/// </summary>
public sealed class JwtFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "http://keycloak.test/realms/enterprise";
    public const string Audience = "crm-api";
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("test-signing-key-test-signing-key-0123456789"));

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:DevHeaders", "false");
        builder.ConfigureServices(s => s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            o.Authority = null;
            o.MetadataAddress = null!;
            o.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration { Issuer = Issuer };
            o.TokenValidationParameters.ValidIssuer = Issuer;
            o.TokenValidationParameters.ValidAudience = Audience;
            o.TokenValidationParameters.IssuerSigningKey = Key;
        }));
    }

    public static string Token(string? issuer = null, string? audience = null, string? sub = "u1", string? tenant = "acme",
        string[]? roles = null, DateTime? expires = null, SecurityKey? key = null)
    {
        var claims = new List<Claim>();
        if (sub is not null) claims.Add(new Claim("sub", sub));
        if (tenant is not null) claims.Add(new Claim("tenant_id", tenant));
        foreach (var r in roles ?? []) claims.Add(new Claim("roles", r));
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(issuer ?? Issuer, audience ?? Audience, claims, now.AddMinutes(-10),
            expires ?? now.AddMinutes(5), new SigningCredentials(key ?? Key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public HttpClient Bearer(string token)
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }
}

public class JwtTests(JwtFactory f) : IClassFixture<JwtFactory>
{
    [Fact]
    public async Task Valid_token_is_accepted_and_claims_are_mapped()
    {
        var res = await f.Bearer(JwtFactory.Token(roles: ["crm-sales"])).GetAsync("/api/v1/whoami");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("acme", body.GetProperty("tenant").GetString());
        Assert.Equal("crm-sales", body.GetProperty("roles")[0].GetString());
    }

    [Fact] public async Task No_token_is_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("/api/v1/whoami")).StatusCode);

    [Fact] public async Task Wrong_issuer_is_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await f.Bearer(JwtFactory.Token(issuer: "http://evil.test")).GetAsync("/api/v1/whoami")).StatusCode);

    [Fact] public async Task Wrong_audience_is_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await f.Bearer(JwtFactory.Token(audience: "erp-api")).GetAsync("/api/v1/whoami")).StatusCode);

    [Fact] public async Task Expired_token_is_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await f.Bearer(JwtFactory.Token(expires: DateTime.UtcNow.AddMinutes(-5))).GetAsync("/api/v1/whoami")).StatusCode);

    [Fact] public async Task Token_signed_with_another_key_is_rejected()
    {
        var other = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("another-signing-key-another-signing-key-9876"));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await f.Bearer(JwtFactory.Token(key: other)).GetAsync("/api/v1/whoami")).StatusCode);
    }

    [Fact] public async Task Support_role_cannot_write() =>
        Assert.Equal(HttpStatusCode.Forbidden,
            (await f.Bearer(JwtFactory.Token(roles: ["crm-support"]))
                .PostAsJsonAsync("/api/v1/customers", new { name = "x", email = "x@y.test" })).StatusCode);

    [Fact] public async Task Erp_role_cannot_use_crm() =>
        Assert.Equal(HttpStatusCode.Forbidden,
            (await f.Bearer(JwtFactory.Token(roles: ["erp-sales"])).GetAsync("/api/v1/customers")).StatusCode);

    [Fact] public async Task Token_without_tenant_cannot_access_tenant_data() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await f.Bearer(JwtFactory.Token(tenant: null, roles: ["crm-admin"])).GetAsync("/api/v1/customers")).StatusCode);

    [Fact] public async Task Service_account_authenticates_but_has_no_crm_data_access() // integration-service, no tenant
    {
        var c = f.Bearer(JwtFactory.Token(sub: "service-account-crm-worker", tenant: null, roles: ["integration-service"]));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/whoami")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/customers")).StatusCode);
    }
}
