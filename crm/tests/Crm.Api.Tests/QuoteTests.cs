using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.Api.Domain;
using Crm.Api.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class StageRuleTests
{
    [Theory]
    [InlineData("Prospecting", "Qualification", true)]
    [InlineData("Prospecting", "Proposal", false)]
    [InlineData("Negotiation", "ClosedLost", true)]
    [InlineData("Proposal", "ClosedWon", false)]
    [InlineData("ClosedWon", "ClosedLost", false)]
    public void Stage_transitions(string from, string to, bool ok) => Assert.Equal(ok, OpportunityStages.CanMove(from, to));
}

// Need PostgreSQL: set CRM_TEST_CONNECTION (see CustomerApiTests).
public class QuoteTests(CrmFactory f) : IClassFixture<CrmFactory>
{
    private static void RequireDb() => Skip.If(CrmFactory.Connection is null, "CRM_TEST_CONNECTION not set");
    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static object Lines(decimal price, int qty = 1) =>
        new { lines = new[] { new { sku = "SKU-1", description = "Widget", quantity = qty, unitPrice = price } } };

    /// <summary>Customer + opportunity + draft quote; optionally links the customer to an ERP id.</summary>
    private async Task<(string Tenant, HttpClient Sales, int CustomerId, int OppId, int QuoteId)> Setup(decimal price, bool synced = true)
    {
        var tenant = $"q-{Guid.NewGuid():N}";
        var sales = f.As(tenant, "alice", "crm-sales");
        var cid = (await Json(await sales.PostAsJsonAsync("/api/v1/customers", CrmFactory.NewCustomer()))).GetProperty("id").GetInt32();
        if (synced)
        {
            using var scope = f.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IntegrationService>()
                .RecordErpCustomerAsync(new RecordErpCustomerInput(tenant, cid, $"ERP-{cid}"), default);
        }

        var oid = (await Json(await sales.PostAsJsonAsync($"/api/v1/customers/{cid}/opportunities", new { name = "Deal", amount = price })))
            .GetProperty("id").GetInt32();
        var q = await sales.PostAsJsonAsync($"/api/v1/opportunities/{oid}/quotes", Lines(price));
        Assert.Equal(HttpStatusCode.Created, q.StatusCode);
        return (tenant, sales, cid, oid, (await Json(q)).GetProperty("quote").GetProperty("id").GetInt32());
    }

    [SkippableFact]
    public async Task Small_quote_is_auto_approved_and_accept_closes_opportunity_and_emits_one_message()
    {
        RequireDb();
        var (tenant, sales, cid, oid, qid) = await Setup(500m);
        var sub = await Json(await sales.PostAsync($"/api/v1/quotes/{qid}/submit", null));
        Assert.Equal("Approved", sub.GetProperty("quote").GetProperty("status").GetString());

        var acc = await sales.PostAsync($"/api/v1/quotes/{qid}/accept", null);
        Assert.Equal(HttpStatusCode.OK, acc.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await sales.PostAsync($"/api/v1/quotes/{qid}/accept", null)).StatusCode); // duplicate accept

        var opp = await Json(await sales.GetAsync($"/api/v1/opportunities/{oid}"));
        Assert.Equal("ClosedWon", opp.GetProperty("stage").GetString());

        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<OutboxPublisher>().PublishBatchAsync(100, default);
        var started = f.Starter.Quotes.Where(d => d.TenantId == tenant && d.QuoteId == qid).ToList();
        Assert.Single(started);
        Assert.Equal($"ERP-{cid}", started[0].ErpCustomerId);
    }

    [SkippableFact]
    public async Task Large_quote_needs_a_different_admin_to_approve()
    {
        RequireDb();
        var (tenant, sales, _, _, qid) = await Setup(50_000m);
        var sub = await Json(await sales.PostAsync($"/api/v1/quotes/{qid}/submit", null));
        Assert.Equal("PendingApproval", sub.GetProperty("quote").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await sales.PostAsync($"/api/v1/quotes/{qid}/accept", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsync($"/api/v1/quotes/{qid}/approve", null)).StatusCode); // sales role
        var selfAdmin = f.As(tenant, "alice", "crm-admin"); // same user id as submitter
        Assert.Equal(HttpStatusCode.Forbidden, (await selfAdmin.PostAsync($"/api/v1/quotes/{qid}/approve", null)).StatusCode);
        var admin = f.As(tenant, "carol", "crm-admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/quotes/{qid}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sales.PostAsync($"/api/v1/quotes/{qid}/accept", null)).StatusCode);
    }

    [SkippableFact]
    public async Task Rejected_quote_cannot_be_accepted()
    {
        RequireDb();
        var (tenant, sales, _, _, qid) = await Setup(50_000m);
        await sales.PostAsync($"/api/v1/quotes/{qid}/submit", null);
        Assert.Equal(HttpStatusCode.OK, (await f.As(tenant, "carol", "crm-admin").PostAsync($"/api/v1/quotes/{qid}/reject", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await sales.PostAsync($"/api/v1/quotes/{qid}/accept", null)).StatusCode);
    }

    [SkippableFact]
    public async Task Accept_requires_customer_synced_to_erp()
    {
        RequireDb();
        var (_, sales, _, _, qid) = await Setup(500m, synced: false);
        await sales.PostAsync($"/api/v1/quotes/{qid}/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, (await sales.PostAsync($"/api/v1/quotes/{qid}/accept", null)).StatusCode);
    }

    [SkippableFact]
    public async Task Only_draft_quotes_can_be_edited_and_lines_replace_total()
    {
        RequireDb();
        var (_, sales, _, _, qid) = await Setup(100m);
        var edited = await Json(await sales.PutAsJsonAsync($"/api/v1/quotes/{qid}/lines", Lines(25m, 4)));
        Assert.Equal(100m, edited.GetProperty("quote").GetProperty("total").GetDecimal());
        Assert.Equal(1, edited.GetProperty("lines").GetArrayLength());
        await sales.PostAsync($"/api/v1/quotes/{qid}/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, (await sales.PutAsJsonAsync($"/api/v1/quotes/{qid}/lines", Lines(1m))).StatusCode);
    }

    [SkippableFact]
    public async Task Erp_order_is_recorded_once_and_appears_in_sales_history()
    {
        RequireDb();
        var (tenant, sales, cid, _, qid) = await Setup(500m);
        await sales.PostAsync($"/api/v1/quotes/{qid}/submit", null);
        await sales.PostAsync($"/api/v1/quotes/{qid}/accept", null);
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<Crm.Api.Application.QuoteService>();
        await svc.RecordErpOrderAsync(new RecordErpOrderInput(tenant, qid, "SO-1"), default);
        await svc.RecordErpOrderAsync(new RecordErpOrderInput(tenant, qid, "SO-1"), default); // duplicate message
        await Assert.ThrowsAsync<Crm.Api.Application.CrmConflictException>(() =>
            svc.RecordErpOrderAsync(new RecordErpOrderInput(tenant, qid, "SO-2"), default));

        var history = await Json(await sales.GetAsync($"/api/v1/customers/{cid}/sales-history"));
        Assert.Equal(1, history.GetArrayLength());
    }

    [SkippableFact]
    public async Task Failed_order_marks_the_quote_once_and_never_overrides_a_real_order()
    {
        RequireDb();
        var (tenant, sales, _, _, qid) = await Setup(500m);
        await sales.PostAsync($"/api/v1/quotes/{qid}/submit", null);
        await sales.PostAsync($"/api/v1/quotes/{qid}/accept", null);
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<Crm.Api.Application.QuoteService>();
        await svc.MarkOrderFailedAsync(new MarkOrderFailedInput(tenant, qid, "Insufficient stock for SKU-1"), default);
        await svc.MarkOrderFailedAsync(new MarkOrderFailedInput(tenant, qid, "Insufficient stock for SKU-1"), default); // duplicate message
        var quote = await Json(await sales.GetAsync($"/api/v1/quotes/{qid}"));
        Assert.Equal("OrderFailed", quote.GetProperty("quote").GetProperty("status").GetString());
        // a failed quote cannot later be recorded as ordered by a stray message
        await Assert.ThrowsAsync<Crm.Api.Application.CrmConflictException>(() =>
            svc.RecordErpOrderAsync(new RecordErpOrderInput(tenant, qid, "SO-1"), default));
        await Assert.ThrowsAsync<Crm.Api.Application.CrmNotFoundException>(() =>
            svc.MarkOrderFailedAsync(new MarkOrderFailedInput("other-tenant", qid, "x"), default));
    }

    [SkippableFact]
    public async Task Ordered_quote_cannot_be_marked_failed()
    {
        RequireDb();
        var (tenant, sales, _, _, qid) = await Setup(500m);
        await sales.PostAsync($"/api/v1/quotes/{qid}/submit", null);
        await sales.PostAsync($"/api/v1/quotes/{qid}/accept", null);
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<Crm.Api.Application.QuoteService>();
        await svc.RecordErpOrderAsync(new RecordErpOrderInput(tenant, qid, "SO-1"), default);
        await Assert.ThrowsAsync<Crm.Api.Application.CrmConflictException>(() =>
            svc.MarkOrderFailedAsync(new MarkOrderFailedInput(tenant, qid, "late failure"), default));
    }

    [SkippableFact]
    public async Task Other_tenant_cannot_see_or_change_quotes()
    {
        RequireDb();
        var (_, _, _, oid, qid) = await Setup(500m);
        var other = f.As($"x-{Guid.NewGuid():N}", "mallory", "crm-admin");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/quotes/{qid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/v1/quotes/{qid}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/opportunities/{oid}")).StatusCode);
    }
}
