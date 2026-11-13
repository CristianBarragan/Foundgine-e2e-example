using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

/// <summary>The CRM MCP endpoint is behind the same authentication and role policy as the REST API and exposes no write tool.</summary>
public sealed class McpFactory : JwtFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Mcp:Enabled", "true");
        base.ConfigureWebHost(builder);
    }
}

public class McpEndpointTests(McpFactory f) : IClassFixture<McpFactory>
{
    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""";
    private const string ListTools = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";

    private static HttpRequestMessage Post(string json)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return req;
    }

    [Fact]
    public async Task Anonymous_call_is_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().SendAsync(Post(Initialize))).StatusCode);

    [Fact]
    public async Task A_token_without_a_crm_role_is_forbidden() =>
        Assert.Equal(HttpStatusCode.Forbidden,
            (await f.Bearer(JwtFactory.Token(roles: ["some-other-role"])).SendAsync(Post(Initialize))).StatusCode);

    [Fact]
    public async Task A_crm_reader_can_initialize()
    {
        var res = await f.Bearer(JwtFactory.Token(roles: ["crm-support"])).SendAsync(Post(Initialize));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("protocolVersion", await res.Content.ReadAsStringAsync()); // JSON or one SSE "data:" frame
    }

    [Fact]
    public async Task Only_read_tools_are_listed()
    {
        var res = await f.Bearer(JwtFactory.Token(roles: ["crm-support"])).SendAsync(Post(ListTools));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = (await res.Content.ReadAsStringAsync()).ToLowerInvariant();
        Assert.Contains("tools", body);
        Assert.DoesNotContain("approved", body);   // FoundgineMcpMutationTools.ExecuteApprovedAsync must not be exposed
        Assert.DoesNotContain("mutation", body);
    }
}
