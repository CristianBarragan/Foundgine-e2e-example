using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.Api.Infrastructure;
using Xunit;

public class CorrelationTests(CrmFactory f) : IClassFixture<CrmFactory>
{
    [Theory]
    [InlineData("abc-123_X.9", "abc-123_X.9")]
    [InlineData("bad id", null)]
    [InlineData("line\nbreak", null)]
    [InlineData("", null)]
    public void Sanitise_keeps_only_safe_ids(string input, string? expected) =>
        Assert.Equal(expected, CorrelationMiddleware.Sanitise(input));

    [Fact]
    public async Task Inbound_id_is_echoed_and_missing_id_is_minted()
    {
        var c = f.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        req.Headers.Add("X-Correlation-Id", "trace-42");
        Assert.Equal("trace-42", (await c.SendAsync(req)).Headers.GetValues("X-Correlation-Id").Single());
        Assert.False(string.IsNullOrEmpty((await c.GetAsync("/api/v1/health")).Headers.GetValues("X-Correlation-Id").Single()));
    }
}

public class RateLimitTests(CrmFactory f) : IClassFixture<CrmFactory>
{
    [Fact]
    public async Task Requests_over_the_limit_get_429()
    {
        var limited = f.WithWebHostBuilder(b => b.UseSetting("RateLimit:PermitsPerMinute", "3"));
        var c = limited.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) codes.Add((await c.GetAsync("/api/v1/health")).StatusCode);
        Assert.Equal(3, codes.Count(x => x == HttpStatusCode.OK));
        Assert.Equal(2, codes.Count(x => x == HttpStatusCode.TooManyRequests));
    }
}

// Need PostgreSQL: set CRM_TEST_CONNECTION (see CustomerApiTests).
public class AuditTests(CrmFactory f) : IClassFixture<CrmFactory>
{
    private static void RequireDb() => Skip.If(CrmFactory.Connection is null, "CRM_TEST_CONNECTION not set");
    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [SkippableFact]
    public async Task Admin_operations_are_audited_per_tenant_with_outcome_and_correlation_id()
    {
        RequireDb();
        var tenant = $"a-{Guid.NewGuid():N}";
        var admin = f.As(tenant, "boss", "crm-admin");
        admin.DefaultRequestHeaders.Add("X-Correlation-Id", "audit-trace-1");
        // Approving a quote that does not exist is a rejected admin operation and must still leave a trace.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/v1/quotes/999999/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/v1/integration/reconcile", null)).StatusCode);

        var rows = await Json(await admin.GetAsync("/api/v1/audit"));
        Assert.Equal(2, rows.GetArrayLength());
        var reconcile = rows[0];
        Assert.Equal("integration.reconcile", reconcile.GetProperty("action").GetString());
        Assert.Equal("Succeeded", reconcile.GetProperty("outcome").GetString());
        Assert.Equal("audit-trace-1", reconcile.GetProperty("correlationId").GetString());
        Assert.Equal("Rejected", rows[1].GetProperty("outcome").GetString());
        Assert.Equal("boss", rows[1].GetProperty("userId").GetString());

        var other = f.As("another-tenant", "x", "crm-admin");
        Assert.Equal(0, (await Json(await other.GetAsync("/api/v1/audit"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await f.As(tenant, "s", "crm-sales").GetAsync("/api/v1/audit")).StatusCode);
    }
}
