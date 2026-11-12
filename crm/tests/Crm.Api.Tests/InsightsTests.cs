using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.Api.Application;
using Crm.Api.Application.Querying;
using Crm.Api.Domain;
using Crm.Api.Integration;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Pure: the lambda filters must produce the same IR as the hand-written CrmSemantics handles.
public class FilterBuilderTests
{
    [Fact]
    public void Eq_resolves_the_semantic_field_from_the_property()
    {
        var f = Assert.IsType<SemanticFieldFilter>(Filter<Customer>.Eq(c => c.Status, "Active"));
        Assert.Equal(FieldId.Create("Customer", "Status"), f.Field);
        Assert.Equal(SemanticFilterOperator.Eq, f.Operator);
        Assert.Equal("Active", f.Value);
    }

    [Fact]
    public void In_accepts_an_array_or_separate_values()
    {
        var a = Assert.IsType<SemanticFieldFilter>(Filter<Opportunity>.In(o => o.Stage, OpportunityStages.Open));
        var b = Assert.IsType<SemanticFieldFilter>(Filter<Quote>.In(q => q.Status, "Accepted", "Ordered"));
        Assert.Equal(4, ((object?[])a.Value!).Length);
        Assert.Equal(new object?[] { "Accepted", "Ordered" }, (object?[])b.Value!);
    }

    [Fact]
    public void Some_and_None_target_the_relationship_named_by_the_navigation()
    {
        var some = Assert.IsType<SemanticRelationshipFilter>(
            Filter<Customer>.Some(c => c.Opportunities, Filter<Opportunity>.Eq(o => o.Stage, "Proposal")));
        var none = Assert.IsType<SemanticRelationshipFilter>(
            Filter<Customer>.None(c => c.Quotes, Filter<Quote>.Eq(q => q.Status, "Ordered")));
        Assert.Equal(RelationshipId.Create("Customer", "Opportunities"), some.Relationship);
        Assert.Equal(SemanticRelationshipQuantifier.Some, some.Quantifier);
        Assert.Equal(RelationshipId.Create("Customer", "Quotes"), none.Relationship);
        Assert.Equal(SemanticRelationshipQuantifier.None, none.Quantifier);
    }

    [Fact]
    public void AtLeast_is_a_count_aggregate_without_a_field()
    {
        var f = Assert.IsType<SemanticAggregateFilter>(
            Filter<Customer>.AtLeast(c => c.Quotes, 3, Filter<Quote>.Eq(q => q.Status, "Ordered")));
        Assert.Equal(SemanticFilterAggregate.Count, f.Aggregate);
        Assert.Null(f.Field);
        Assert.Equal(SemanticAggregateFilterOperator.Gte, f.Operator);
        Assert.Equal(3, f.Value);
    }

    [Fact]
    public void A_non_member_selector_is_rejected() =>
        Assert.Throws<ArgumentException>(() => Filter<Customer>.Eq(c => c.Name.Trim(), "x"));
}

// Need PostgreSQL: set CRM_TEST_CONNECTION (see CustomerApiTests).
public class InsightsTests(CrmFactory f) : IClassFixture<CrmFactory>
{
    private static void RequireDb() => Skip.If(CrmFactory.Connection is null, "CRM_TEST_CONNECTION not set");
    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();
    private static object Lines(decimal price) =>
        new { lines = new[] { new { sku = "SKU-1", description = "Widget", quantity = 1, unitPrice = price } } };

    private async Task<int> NewCustomer(HttpClient sales, string tenant, bool synced = true)
    {
        var id = (await Json(await sales.PostAsJsonAsync("/api/v1/customers", CrmFactory.NewCustomer()))).GetProperty("id").GetInt32();
        if (synced)
        {
            using var scope = f.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IntegrationService>()
                .RecordErpCustomerAsync(new RecordErpCustomerInput(tenant, id, $"ERP-{id}"), default);
        }

        return id;
    }

    private static async Task<int> NewOpportunity(HttpClient sales, int customerId, decimal amount) =>
        (await Json(await sales.PostAsJsonAsync($"/api/v1/customers/{customerId}/opportunities", new { name = "Deal", amount })))
        .GetProperty("id").GetInt32();

    private static async Task<int> NewQuote(HttpClient sales, int opportunityId, decimal price) =>
        (await Json(await sales.PostAsJsonAsync($"/api/v1/opportunities/{opportunityId}/quotes", Lines(price))))
        .GetProperty("quote").GetProperty("id").GetInt32();

    // Semantic rows are dictionaries keyed by field name; read the key without depending on its casing.
    private static async Task<int[]> Ids(HttpClient c, string url) =>
        (await Json(await c.GetAsync(url))).EnumerateArray()
        .Select(x => x.EnumerateObject().First(p => string.Equals(p.Name, "id", StringComparison.OrdinalIgnoreCase)).Value.GetInt32())
        .ToArray();

    [SkippableFact]
    public async Task At_risk_means_open_deal_no_accepted_quote_and_a_failed_one()
    {
        RequireDb();
        var tenant = $"ins-{Guid.NewGuid():N}";
        var sales = f.As(tenant, "alice", "crm-sales");
        var admin = f.As(tenant, "carol", "crm-admin");

        var atRisk = await NewCustomer(sales, tenant);
        var rejectedOpp = await NewOpportunity(sales, atRisk, 50_000m);
        var rejectedQuote = await NewQuote(sales, rejectedOpp, 50_000m);
        await sales.PostAsync($"/api/v1/quotes/{rejectedQuote}/submit", null);
        await admin.PostAsync($"/api/v1/quotes/{rejectedQuote}/reject", null);
        await NewOpportunity(sales, atRisk, 100m); // still open

        var won = await NewCustomer(sales, tenant);
        var wonQuote = await NewQuote(sales, await NewOpportunity(sales, won, 500m), 500m);
        await sales.PostAsync($"/api/v1/quotes/{wonQuote}/submit", null);
        await sales.PostAsync($"/api/v1/quotes/{wonQuote}/accept", null);

        var noFailure = await NewCustomer(sales, tenant);
        await NewOpportunity(sales, noFailure, 100m);

        var ids = await Ids(sales, "/api/v1/insights/at-risk-customers");
        Assert.Equal(new[] { atRisk }, ids);
    }

    [SkippableFact]
    public async Task Key_accounts_need_the_requested_number_of_ordered_quotes()
    {
        RequireDb();
        var tenant = $"ins-{Guid.NewGuid():N}";
        var sales = f.As(tenant, "alice", "crm-sales");
        var cid = await NewCustomer(sales, tenant);
        using var scope = f.Services.CreateScope();
        var quotes = scope.ServiceProvider.GetRequiredService<QuoteService>();
        for (var n = 0; n < 2; n++)
        {
            var q = await NewQuote(sales, await NewOpportunity(sales, cid, 100m), 100m);
            await sales.PostAsync($"/api/v1/quotes/{q}/submit", null);
            await sales.PostAsync($"/api/v1/quotes/{q}/accept", null);
            await quotes.RecordErpOrderAsync(new RecordErpOrderInput(tenant, q, $"ORD-{q}"), default);
        }

        Assert.Equal(new[] { cid }, await Ids(sales, "/api/v1/insights/key-accounts?minOrders=2"));
        Assert.Empty(await Ids(sales, "/api/v1/insights/key-accounts?minOrders=3"));
    }

    [SkippableFact]
    public async Task Integration_attention_lists_pending_and_failed_but_not_active()
    {
        RequireDb();
        var tenant = $"ins-{Guid.NewGuid():N}";
        var sales = f.As(tenant, "alice", "crm-sales");
        var pending = await NewCustomer(sales, tenant, synced: false);
        await NewCustomer(sales, tenant); // Active
        Assert.Equal(new[] { pending }, await Ids(sales, "/api/v1/insights/integration-attention"));
    }

    [SkippableFact]
    public async Task Pipeline_sums_by_stage_and_computes_win_rate()
    {
        RequireDb();
        var tenant = $"ins-{Guid.NewGuid():N}";
        var sales = f.As(tenant, "alice", "crm-sales");
        var cid = await NewCustomer(sales, tenant);
        await NewOpportunity(sales, cid, 100m);
        await NewOpportunity(sales, cid, 250m);
        var wonOpp = await NewOpportunity(sales, cid, 500m);
        var q = await NewQuote(sales, wonOpp, 500m);
        await sales.PostAsync($"/api/v1/quotes/{q}/submit", null);
        await sales.PostAsync($"/api/v1/quotes/{q}/accept", null);

        var report = await Json(await sales.GetAsync("/api/v1/insights/pipeline"));
        Assert.Equal(350m, report.GetProperty("openPipeline").GetDecimal());
        Assert.Equal(500m, report.GetProperty("wonAmount").GetDecimal());
        Assert.Equal(1.0, report.GetProperty("winRate").GetDouble());
    }

    [SkippableFact]
    public async Task Receivables_subtract_received_payments_and_age_by_due_date()
    {
        RequireDb();
        var tenant = $"ins-{Guid.NewGuid():N}";
        var sales = f.As(tenant, "alice", "crm-sales");
        var cid = await NewCustomer(sales, tenant);
        var q = await NewQuote(sales, await NewOpportunity(sales, cid, 100m), 100m);
        await sales.PostAsync($"/api/v1/quotes/{q}/submit", null);
        await sales.PostAsync($"/api/v1/quotes/{q}/accept", null);
        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<QuoteService>()
            .RecordErpOrderAsync(new RecordErpOrderInput(tenant, q, $"ORD-{q}"), default);
        var fulfil = scope.ServiceProvider.GetRequiredService<FulfilmentService>();
        var order = $"ORD-{q}";
        await fulfil.RecordInvoiceAsync(new RecordInvoiceInput(tenant, order, "INV-1", "0001", 100m, "Issued",
            DateTime.UtcNow.AddDays(-70), DateTime.UtcNow.AddDays(-40), 1), default);
        await fulfil.RecordPaymentAsync(new RecordPaymentInput(tenant, order, "INV-1", "PAY-1", 30m, "Received", 1), default);
        await fulfil.RecordPaymentAsync(new RecordPaymentInput(tenant, order, "INV-1", "PAY-2", 500m, "Reversed", 1), default);

        var rows = await Json(await sales.GetAsync("/api/v1/insights/receivables"));
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(70m, rows[0].GetProperty("outstanding").GetDecimal());
        var aging = rows[0].GetProperty("aging");
        Assert.Equal("31-60", aging[0].GetProperty("bucket").GetString());
        Assert.Equal(70m, aging[0].GetProperty("amount").GetDecimal());
    }

    [SkippableFact]
    public async Task Customer_360_assembles_the_customer_and_hides_other_tenants()
    {
        RequireDb();
        var tenant = $"ins-{Guid.NewGuid():N}";
        var sales = f.As(tenant, "alice", "crm-sales");
        var cid = await NewCustomer(sales, tenant);
        await sales.PostAsJsonAsync($"/api/v1/customers/{cid}/contacts",
            new { firstName = "Ann", lastName = "Lee", email = "ann@acme.test", isPrimary = true });
        var q = await NewQuote(sales, await NewOpportunity(sales, cid, 100m), 100m);

        var res = await sales.GetAsync($"/api/v1/customers/{cid}/360");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await Json(res);
        Assert.Equal(cid, body.GetProperty("customer").GetProperty("id").GetInt32());
        Assert.Equal("Active", body.GetProperty("integration").GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("opportunities").GetArrayLength());
        Assert.Equal(q, body.GetProperty("quotes")[0].GetProperty("quote").GetProperty("id").GetInt32());
        Assert.Equal(1, body.GetProperty("quotes")[0].GetProperty("lines").GetArrayLength());

        var other = f.As($"ins-{Guid.NewGuid():N}", "mallory", "crm-sales");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/customers/{cid}/360")).StatusCode);
    }

    [SkippableFact]
    public async Task Insights_are_tenant_scoped_and_need_the_read_role()
    {
        RequireDb();
        var tenant = $"ins-{Guid.NewGuid():N}";
        var sales = f.As(tenant, "alice", "crm-sales");
        await NewCustomer(sales, tenant, synced: false);
        var other = f.As($"ins-{Guid.NewGuid():N}", "mallory", "crm-sales");
        Assert.Empty(await Ids(other, "/api/v1/insights/integration-attention"));
        var nobody = f.As(tenant, "eve");
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync("/api/v1/insights/pipeline")).StatusCode);
    }
}
