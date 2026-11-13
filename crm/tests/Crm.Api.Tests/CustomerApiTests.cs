using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

// Need a SQL Server database: set CRM_TEST_CONNECTION, e.g.
//   Server=localhost,1433;Database=crm_test;User Id=sa;Password=Crm_dev_pw1!;TrustServerCertificate=True
public class CustomerApiTests(CrmFactory f) : IClassFixture<CrmFactory>
{
    private static readonly string Tenant = $"t-{Guid.NewGuid():N}";

    private static void RequireDb() => Skip.If(CrmFactory.Connection is null, "CRM_TEST_CONNECTION not set");

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>());

    [SkippableFact]
    public async Task Create_get_update_customer_with_audit()
    {
        RequireDb();
        var sales = f.As(Tenant, "alice", "crm-sales");
        var created = await sales.PostAsJsonAsync("/api/v1/customers", CrmFactory.NewCustomer());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var c = await Json(created);
        var id = c.GetProperty("id").GetInt32();
        Assert.Equal("alice", c.GetProperty("createdBy").GetString());

        var updated = await f.As(Tenant, "bob", "crm-admin")
            .PutAsJsonAsync($"/api/v1/customers/{id}", new { name = "Acme Holdings" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var u = await Json(updated);
        Assert.Equal("Acme Holdings", u.GetProperty("name").GetString());
        Assert.Equal("alice", u.GetProperty("createdBy").GetString());
        Assert.Equal("bob", u.GetProperty("updatedBy").GetString());

        var got = await f.As(Tenant, "carol", "crm-support").GetAsync($"/api/v1/customers/{id}");
        Assert.Equal(HttpStatusCode.OK, got.StatusCode);
    }

    [SkippableFact]
    public async Task Duplicate_email_in_same_tenant_conflicts()
    {
        RequireDb();
        var sales = f.As(Tenant, "alice", "crm-sales");
        var body = CrmFactory.NewCustomer($"dup-{Guid.NewGuid():N}@acme.test");
        Assert.Equal(HttpStatusCode.Created, (await sales.PostAsJsonAsync("/api/v1/customers", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await sales.PostAsJsonAsync("/api/v1/customers", body)).StatusCode);
    }

    [SkippableFact]
    public async Task Other_tenant_cannot_read_update_or_see_customer()
    {
        RequireDb();
        var mine = f.As(Tenant, "alice", "crm-admin");
        var id = (await Json(await mine.PostAsJsonAsync("/api/v1/customers", CrmFactory.NewCustomer())))
            .GetProperty("id").GetInt32();

        var other = f.As($"other-{Guid.NewGuid():N}", "mallory", "crm-admin");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/customers/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync($"/api/v1/customers/{id}", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/customers/{id}/contacts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/customers/{id}/contacts",
            new { firstName = "A", lastName = "B", email = "a@b.test" })).StatusCode);
        var search = await Json(await other.GetAsync("/api/v1/customers"));
        Assert.Equal(0, search.GetProperty("items").GetArrayLength());
    }

    [SkippableFact]
    public async Task Contacts_are_created_and_listed_for_a_customer()
    {
        RequireDb();
        var sales = f.As(Tenant, "alice", "crm-sales");
        var id = (await Json(await sales.PostAsJsonAsync("/api/v1/customers", CrmFactory.NewCustomer())))
            .GetProperty("id").GetInt32();
        var created = await sales.PostAsJsonAsync($"/api/v1/customers/{id}/contacts",
            new { firstName = "Jane", lastName = "Doe", email = "jane@acme.test", isPrimary = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var contactId = (await Json(created)).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await sales.GetAsync($"/api/v1/contacts/{contactId}")).StatusCode);
        var list = await Json(await sales.GetAsync($"/api/v1/customers/{id}/contacts"));
        Assert.Equal(1, list.GetArrayLength());
    }

    [SkippableFact]
    public async Task Search_by_exact_filter_and_partial_text()
    {
        RequireDb();
        var tenant = $"s-{Guid.NewGuid():N}";
        var sales = f.As(tenant, "alice", "crm-sales");
        await sales.PostAsJsonAsync("/api/v1/customers",
            new { kind = "Business", name = "Globex Corporation", email = "info@globex.test" });
        await sales.PostAsJsonAsync("/api/v1/customers",
            new { kind = "Individual", name = "Hank Scorpio", email = "hank@example.test" });

        var byEmail = await Json(await sales.GetAsync("/api/v1/customers?email=info@globex.test"));
        Assert.Equal(1, byEmail.GetProperty("items").GetArrayLength());

        var byText = await Json(await sales.GetAsync("/api/v1/customers?q=glob"));
        Assert.Equal(1, byText.GetProperty("items").GetArrayLength());

        var all = await Json(await sales.GetAsync("/api/v1/customers"));
        Assert.Equal(2, all.GetProperty("items").GetArrayLength());
    }
}
