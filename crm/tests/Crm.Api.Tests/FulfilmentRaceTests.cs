using Crm.Api.Application;
using Crm.Api.Integration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

/// <summary>
/// Regression for the lost-update race: two FIRST deliveries of one entity (v1 and v2) run in parallel. Before the fix the
/// insert loser swallowed its unique-key violation assuming the winner was newer, so when v2 lost to v1 the CRM stayed on v1.
/// The race is probabilistic, so each test repeats it across many fresh entities; with the bug at least one lands on v1.
/// </summary>
public class FulfilmentRaceTests(CrmFactory f) : IClassFixture<CrmFactory>
{
    private const int Rounds = 40;
    private static void RequireDb() => Skip.If(CrmFactory.Connection is null, "CRM_TEST_CONNECTION not set");
    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(string Tenant, HttpClient Sales, int QuoteId, string ErpOrderId)> OrderedQuote()
    {
        var tenant = $"race-{Guid.NewGuid():N}";
        var sales = f.As(tenant, "alice", "crm-sales");
        var cid = (await Json(await sales.PostAsJsonAsync("/api/v1/customers", CrmFactory.NewCustomer()))).GetProperty("id").GetInt32();
        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IntegrationService>()
            .RecordErpCustomerAsync(new RecordErpCustomerInput(tenant, cid, $"ERP-{cid}"), default);
        var oid = (await Json(await sales.PostAsJsonAsync($"/api/v1/customers/{cid}/opportunities", new { name = "Deal", amount = 100 }))).GetProperty("id").GetInt32();
        var body = new { lines = new[] { new { sku = "SKU-1", description = "Widget", quantity = 5, unitPrice = 20m } } };
        var qid = (await Json(await sales.PostAsJsonAsync($"/api/v1/opportunities/{oid}/quotes", body))).GetProperty("quote").GetProperty("id").GetInt32();
        (await sales.PostAsync($"/api/v1/quotes/{qid}/submit", null)).EnsureSuccessStatusCode();
        (await sales.PostAsync($"/api/v1/quotes/{qid}/accept", null)).EnsureSuccessStatusCode();
        var erpOrder = $"ORD-{qid}";
        await scope.ServiceProvider.GetRequiredService<QuoteService>().RecordErpOrderAsync(new RecordErpOrderInput(tenant, qid, erpOrder), default);
        return (tenant, sales, qid, erpOrder);
    }

    /// <summary>Runs both deliveries concurrently, each in its own scope (own DbContext), released together by a gate.</summary>
    private async Task Race(Func<FulfilmentService, Task> older, Func<FulfilmentService, Task> newer)
    {
        var gate = new TaskCompletionSource();
        async Task Run(Func<FulfilmentService, Task> call)
        {
            using var scope = f.Services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<FulfilmentService>();
            await gate.Task;
            await call(svc);
        }
        var tasks = new[] { Task.Run(() => Run(older)), Task.Run(() => Run(newer)) };
        gate.SetResult();
        await Task.WhenAll(tasks);
    }

    [SkippableFact]
    public async Task Parallel_first_deliveries_of_a_shipment_always_end_on_the_newest_version()
    {
        RequireDb();
        var (tenant, sales, qid, order) = await OrderedQuote();
        for (var i = 0; i < Rounds; i++)
        {
            var id = $"SHP-{i}";
            await Race(
                s => s.RecordShipmentAsync(new RecordShipmentInput(tenant, order, id, "Packed", "NZ Post", "T", 1), default),
                s => s.RecordShipmentAsync(new RecordShipmentInput(tenant, order, id, "Delivered", "NZ Post", "T", 2), default));
        }
        var shipments = (await Json(await sales.GetAsync($"/api/v1/quotes/{qid}/fulfilment"))).GetProperty("shipments");
        Assert.Equal(Rounds, shipments.GetArrayLength());
        Assert.All(shipments.EnumerateArray(), s =>
        {
            Assert.Equal("Delivered", s.GetProperty("status").GetString());
            Assert.Equal(2, s.GetProperty("version").GetInt32());
        });
    }

    [SkippableFact]
    public async Task Parallel_first_deliveries_of_an_invoice_always_end_on_the_newest_version()
    {
        RequireDb();
        var (tenant, sales, qid, order) = await OrderedQuote();
        var issued = new DateTime(2026, 11, 13, 1, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < Rounds; i++)
        {
            var id = $"INV-{i}";
            await Race(
                s => s.RecordInvoiceAsync(new RecordInvoiceInput(tenant, order, id, $"N-{i}", 100m, "Issued", issued, null, 1), default),
                s => s.RecordInvoiceAsync(new RecordInvoiceInput(tenant, order, id, $"N-{i}", 100m, "Paid", issued, null, 2), default));
        }
        var invoices = (await Json(await sales.GetAsync($"/api/v1/quotes/{qid}/fulfilment"))).GetProperty("invoices");
        Assert.Equal(Rounds, invoices.GetArrayLength());
        Assert.All(invoices.EnumerateArray(), x =>
        {
            Assert.Equal("Paid", x.GetProperty("status").GetString());
            Assert.Equal(2, x.GetProperty("version").GetInt32());
        });
    }

    [SkippableFact]
    public async Task Parallel_first_deliveries_of_a_payment_always_end_on_the_newest_version()
    {
        RequireDb();
        var (tenant, sales, qid, order) = await OrderedQuote();
        for (var i = 0; i < Rounds; i++)
        {
            var id = $"PAY-{i}";
            await Race(
                s => s.RecordPaymentAsync(new RecordPaymentInput(tenant, order, "INV-X", id, 100m, "Received", 1), default),
                s => s.RecordPaymentAsync(new RecordPaymentInput(tenant, order, "INV-X", id, 100m, "Reconciled", 2), default));
        }
        var payments = (await Json(await sales.GetAsync($"/api/v1/quotes/{qid}/fulfilment"))).GetProperty("payments");
        Assert.Equal(Rounds, payments.GetArrayLength());
        Assert.All(payments.EnumerateArray(), p =>
        {
            Assert.Equal("Reconciled", p.GetProperty("status").GetString());
            Assert.Equal(2, p.GetProperty("version").GetInt32());
        });
    }
}
