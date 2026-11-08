using System.Net;
using System.Net.Http.Json;
using Xunit;

// These run without a database: authentication/authorization fail before any data access.
public class AuthorizationTests(CrmFactory f) : IClassFixture<CrmFactory>
{
    [Fact]
    public async Task Anonymous_is_rejected()
    {
        var res = await f.CreateClient().GetAsync("/api/v1/customers");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Support_cannot_create_customers()
    {
        var res = await f.As("t1", "u1", "crm-support").PostAsJsonAsync("/api/v1/customers", CrmFactory.NewCustomer());
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task User_without_crm_role_cannot_read()
    {
        var res = await f.As("t1", "u1", "erp-sales").GetAsync("/api/v1/customers/1");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Invalid_customer_is_rejected_before_data_access()
    {
        var res = await f.As("t1", "u1", "crm-sales").PostAsJsonAsync("/api/v1/customers",
            new { kind = "Business", name = "", email = "not-an-email" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
