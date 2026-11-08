using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.Api.Application;
using Crm.Api.Domain;
using Crm.Api.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class ReturnStatusRuleTests
{
    [Theory]
    [InlineData("Requested", "Authorized", true)]
    [InlineData("Requested", "Rejected", true)]
    [InlineData("Authorized", "Completed", true)]
    [InlineData("Requested", "Completed", false)]
    [InlineData("Rejected", "Authorized", false)]
    [InlineData("Completed", "Authorized", false)]
    public void Return_transitions(string from, string to, bool ok) => Assert.Equal(ok, ReturnStatuses.CanMove(from, to));

    [Fact] public void Return_workflow_id_is_stable() =>
        Assert.Equal("ReturnProcessing:acme:7", FulfilmentIntegrationNames.ReturnWorkflowId("acme", 7));
}

// Need PostgreSQL: set CRM_TEST_CONNECTION (see CustomerApiTests).
public class FulfilmentTests(CrmFactory f) : IClassFixture<CrmFactory>
{
    private static void RequireDb() => Skip.If(CrmFactory.Connection is null, "CRM_TEST_CONNECTION not set");
    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>A quote that went through the whole Phase 7 flow and is Ordered as ERP order "ORD-&lt;quoteId&gt;".</summary>
    private async Task<(string Tenant, HttpClient Sales, int CustomerId, int QuoteId, string ErpOrderId)> OrderedQuote()
    {
        var tenant = $"f-{Guid.NewGuid():N}";
        var sales = f.As(tenant, "alice", "crm-sales");
        var cid = (await Json(await sales.PostAsJsonAsync("/api/v1/customers", CrmFactory.NewCustomer()))).GetProperty("id").GetInt32();
        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IntegrationService>()
            .RecordErpCustomerAsync(new RecordErpCustomerInput(tenant, cid, $"ERP-{cid}"), default);
        var oid = (await Json(await sales.PostAsJsonAsync($"/api/v1/customers/{cid}/opportunities", new { name = "Deal", amount = 100 }))).GetProperty("id").GetInt32();
        var body = new { lines = new[] { new { sku = "SKU-1", description = "Widget", quantity = 5, unitPrice = 20m } } };
        var qid = (await Json(await sales.PostAsJsonAsync($"/api/v1/opportunities/{oid}/quotes", body))).GetProperty("quote").GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await sales.PostAsync($"/api/v1/quotes/{qid}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sales.PostAsync($"/api/v1/quotes/{qid}/accept", null)).StatusCode);
        var erpOrder = $"ORD-{qid}";
        await scope.ServiceProvider.GetRequiredService<QuoteService>().RecordErpOrderAsync(new RecordErpOrderInput(tenant, qid, erpOrder), default);
        return (tenant, sales, cid, qid, erpOrder);
    }

    private static RecordShipmentInput Shipment(string tenant, string order, string status, int version) =>
        new(tenant, order, "SHP-1", status, "NZ Post", "TRK-1", version);

    [SkippableFact]
    public async Task Shipment_updates_are_idempotent_and_never_go_backwards()
    {
        RequireDb();
        var (tenant, sales, _, qid, order) = await OrderedQuote();
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<FulfilmentService>();
        await svc.RecordShipmentAsync(Shipment(tenant, order, "Shipped", 2), default);
        await svc.RecordShipmentAsync(Shipment(tenant, order, "Shipped", 2), default);   // duplicate delivery
        await svc.RecordShipmentAsync(Shipment(tenant, order, "Packed", 1), default);    // stale, out of order
        var s = (await Json(await sales.GetAsync($"/api/v1/quotes/{qid}/fulfilment"))).GetProperty("shipments");
        Assert.Equal(1, s.GetArrayLength());
        Assert.Equal("Shipped", s[0].GetProperty("status").GetString());
        await svc.RecordShipmentAsync(Shipment(tenant, order, "Delivered", 3), default);
        s = (await Json(await sales.GetAsync($"/api/v1/quotes/{qid}/fulfilment"))).GetProperty("shipments");
        Assert.Equal("Delivered", s[0].GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Unknown_erp_order_is_not_found_so_the_activity_retries()
    {
        RequireDb();
        var (tenant, _, _, _, _) = await OrderedQuote();
        using var scope = f.Services.CreateScope();
        await Assert.ThrowsAsync<CrmNotFoundException>(() =>
            scope.ServiceProvider.GetRequiredService<FulfilmentService>().RecordShipmentAsync(Shipment(tenant, "ORD-nope", "Shipped", 1), default));
    }

    [SkippableFact]
    public async Task Invoice_and_payment_show_up_on_the_quote_and_the_customer()
    {
        RequireDb();
        var (tenant, sales, cid, qid, order) = await OrderedQuote();
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<FulfilmentService>();
        await svc.RecordInvoiceAsync(new RecordInvoiceInput(tenant, order, "INV-1", "2026-0001", 100m, "Issued", DateTime.UtcNow, DateTime.UtcNow.AddDays(30), 1), default);
        await svc.RecordPaymentAsync(new RecordPaymentInput(tenant, order, "INV-1", "PAY-1", 100m, "Received", 1), default);
        await svc.RecordInvoiceAsync(new RecordInvoiceInput(tenant, order, "INV-1", "2026-0001", 100m, "Paid", DateTime.UtcNow, null, 2), default);

        var view = await Json(await sales.GetAsync($"/api/v1/quotes/{qid}/fulfilment"));
        Assert.Equal(order, view.GetProperty("erpOrderId").GetString());
        Assert.Equal("Paid", view.GetProperty("invoices")[0].GetProperty("status").GetString());
        Assert.Equal(1, view.GetProperty("payments").GetArrayLength());
        var inv = await Json(await sales.GetAsync($"/api/v1/customers/{cid}/invoices"));
        Assert.Equal(1, inv.GetArrayLength());
    }

    [SkippableFact]
    public async Task Fulfilment_is_tenant_isolated()
    {
        RequireDb();
        var (_, _, cid, qid, _) = await OrderedQuote();
        var other = f.As("another-tenant", "mallory", "crm-sales");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/quotes/{qid}/fulfilment")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/customers/{cid}/invoices")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/quotes/{qid}/returns",
            new { reason = "x", lines = new[] { new { sku = "SKU-1", quantity = 1 } } })).StatusCode);
    }

    [SkippableFact]
    public async Task Return_request_is_capped_by_ordered_quantity_and_emits_one_message()
    {
        RequireDb();
        var (tenant, sales, _, qid, order) = await OrderedQuote();
        HttpResponseMessage Ask(int qty, string sku = "SKU-1") => sales.PostAsJsonAsync($"/api/v1/quotes/{qid}/returns",
            new { reason = "Damaged", lines = new[] { new { sku, quantity = qty } } }).Result;

        Assert.Equal(HttpStatusCode.BadRequest, Ask(6).StatusCode);            // ordered 5
        Assert.Equal(HttpStatusCode.BadRequest, Ask(1, "SKU-OTHER").StatusCode); // not on the quote
        var ok = Ask(3);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var rid = (await Json(ok)).GetProperty("return").GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, Ask(3).StatusCode);            // only 2 remain
        Assert.Equal(HttpStatusCode.Created, Ask(2).StatusCode);

        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<OutboxPublisher>().PublishBatchAsync(100, default);
        var started = f.Starter.Returns.Where(d => d.TenantId == tenant && d.QuoteId == qid).ToList();
        Assert.Equal(2, started.Count);
        Assert.Equal(order, started.Single(d => d.ReturnId == rid).ErpOrderId);
    }

    [SkippableFact]
    public async Task Return_decisions_follow_the_state_machine_and_are_idempotent()
    {
        RequireDb();
        var (tenant, sales, _, qid, _) = await OrderedQuote();
        var created = await sales.PostAsJsonAsync($"/api/v1/quotes/{qid}/returns",
            new { reason = "Wrong item", lines = new[] { new { sku = "SKU-1", quantity = 1 } } });
        var rid = (await Json(created)).GetProperty("return").GetProperty("id").GetInt32();
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<FulfilmentService>();

        await Assert.ThrowsAsync<CrmConflictException>(() =>
            svc.RecordReturnDecisionAsync(new RecordReturnDecisionInput(tenant, rid, "Completed", null, null), default)); // skips Authorized
        await Assert.ThrowsAsync<CrmValidationException>(() =>
            svc.RecordReturnDecisionAsync(new RecordReturnDecisionInput(tenant, rid, "Authorized", null, null), default)); // no ERP id
        var auth = new RecordReturnDecisionInput(tenant, rid, "Authorized", "RMA-77", "ok");
        await svc.RecordReturnDecisionAsync(auth, default);
        await svc.RecordReturnDecisionAsync(auth, default); // duplicate message
        await Assert.ThrowsAsync<CrmConflictException>(() =>
            svc.RecordReturnDecisionAsync(auth with { ErpReturnId = "RMA-78" }, default));
        await svc.RecordReturnDecisionAsync(new RecordReturnDecisionInput(tenant, rid, "Completed", null, null), default);

        var r = (await Json(await sales.GetAsync($"/api/v1/returns/{rid}"))).GetProperty("return");
        Assert.Equal("Completed", r.GetProperty("status").GetString());
        Assert.Equal("RMA-77", r.GetProperty("erpReturnId").GetString());
    }

    [Fact]
    public async Task Support_role_can_read_but_not_request_returns()
    {
        var support = f.As("t", "sam", "crm-support");
        var res = await support.PostAsJsonAsync("/api/v1/quotes/1/returns", new { reason = "x", lines = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
