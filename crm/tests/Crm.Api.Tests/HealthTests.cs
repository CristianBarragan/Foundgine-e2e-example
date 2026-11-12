using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_returns_ok()
    {
        var res = await factory.CreateClient().GetAsync("/api/v1/health");
        res.EnsureSuccessStatusCode();
    }
}
