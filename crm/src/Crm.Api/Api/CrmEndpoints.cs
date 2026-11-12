using Crm.Api.Application;

namespace Crm.Api.Api;

public static class CrmEndpoints
{
    public static void MapCrmApi(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1");
        v1.MapGet("/health", () => Results.Ok(new { service = "crm", status = "ok" })).AllowAnonymous();

        // Any authenticated caller (user or service account): shows what the token grants. Handy for Phase 2 checks.
        v1.MapGet("/whoami", (HttpContext ctx) => Results.Ok(new
        {
            subject = ctx.User.FindFirst("sub")?.Value,
            tenant = ctx.User.FindFirst(HttpTenantContext.TenantClaim)?.Value,
            roles = ctx.User.FindAll("roles").Select(c => c.Value).ToArray()
        }));

        var customers = v1.MapGroup("/customers");
        customers.MapPost("", async (CustomerRequest r, CustomerService s, CancellationToken ct) =>
        {
            var c = await s.CreateAsync(r, ct);
            return Results.Created($"/api/v1/customers/{c.Id}", c);
        }).RequireAuthorization(CrmRoles.WritePolicy);

        customers.MapGet("{id:int}", async (int id, CustomerService s, CancellationToken ct) =>
            Results.Ok(await s.GetAsync(id, ct))).RequireAuthorization(CrmRoles.ReadPolicy);

        customers.MapPut("{id:int}", async (int id, CustomerRequest r, CustomerService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(id, r, ct))).RequireAuthorization(CrmRoles.WritePolicy);

        customers.MapGet("", async (string? q, string? email, string? status, string? kind, int? skip, int? take,
                CustomerService s, CancellationToken ct) =>
                Results.Ok(await s.SearchAsync(q, email, status, kind, skip ?? 0, take ?? 25, ct)))
            .RequireAuthorization(CrmRoles.ReadPolicy);

        customers.MapPost("{id:int}/contacts", async (int id, ContactRequest r, ContactService s, CancellationToken ct) =>
        {
            var c = await s.CreateAsync(id, r, ct);
            return Results.Created($"/api/v1/contacts/{c.Id}", c);
        }).RequireAuthorization(CrmRoles.WritePolicy);

        customers.MapGet("{id:int}/contacts", async (int id, ContactService s, CancellationToken ct) =>
            Results.Ok(await s.ListForCustomerAsync(id, ct))).RequireAuthorization(CrmRoles.ReadPolicy);

        // Opportunities
        customers.MapPost("{id:int}/opportunities", async (int id, OpportunityRequest r, OpportunityService s, CancellationToken ct) =>
        {
            var o = await s.CreateAsync(id, r, ct);
            return Results.Created($"/api/v1/opportunities/{o.Id}", o);
        }).RequireAuthorization(CrmRoles.WritePolicy);
        customers.MapGet("{id:int}/opportunities", async (int id, OpportunityService s, CancellationToken ct) =>
            Results.Ok(await s.ListForCustomerAsync(id, ct))).RequireAuthorization(CrmRoles.ReadPolicy);
        customers.MapGet("{id:int}/sales-history", async (int id, QuoteService s, CancellationToken ct) =>
            Results.Ok(await s.SalesHistoryAsync(id, ct))).RequireAuthorization(CrmRoles.ReadPolicy);
        v1.MapGet("/opportunities/{id:int}", async (int id, OpportunityService s, CancellationToken ct) =>
            Results.Ok(await s.GetAsync(id, ct))).RequireAuthorization(CrmRoles.ReadPolicy);
        v1.MapPut("/opportunities/{id:int}", async (int id, OpportunityRequest r, OpportunityService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(id, r, ct))).RequireAuthorization(CrmRoles.WritePolicy);

        // Quotes
        v1.MapPost("/opportunities/{id:int}/quotes", async (int id, QuoteRequest r, QuoteService s, CancellationToken ct) =>
        {
            var q = await s.CreateAsync(id, r, ct);
            return Results.Created($"/api/v1/quotes/{q.Quote.Id}", q);
        }).RequireAuthorization(CrmRoles.WritePolicy);
        var quotes = v1.MapGroup("/quotes");
        quotes.MapGet("{id:int}", async (int id, QuoteService s, CancellationToken ct) =>
            Results.Ok(await s.GetAsync(id, ct))).RequireAuthorization(CrmRoles.ReadPolicy);
        quotes.MapPut("{id:int}/lines", async (int id, QuoteRequest r, QuoteService s, CancellationToken ct) =>
            Results.Ok(await s.ReplaceLinesAsync(id, r, ct))).RequireAuthorization(CrmRoles.WritePolicy);
        quotes.MapPost("{id:int}/submit", async (int id, QuoteService s, CancellationToken ct) =>
            Results.Ok(await s.SubmitAsync(id, ct))).RequireAuthorization(CrmRoles.WritePolicy);
        quotes.MapPost("{id:int}/approve", async (int id, QuoteService s, CancellationToken ct) =>
            Results.Ok(await s.ApproveAsync(id, ct))).RequireAuthorization(CrmRoles.AdminPolicy)
            .AddEndpointFilter(new Crm.Api.Infrastructure.AuditFilter("quote.approve"));
        quotes.MapPost("{id:int}/reject", async (int id, QuoteService s, CancellationToken ct) =>
            Results.Ok(await s.RejectAsync(id, ct))).RequireAuthorization(CrmRoles.AdminPolicy)
            .AddEndpointFilter(new Crm.Api.Infrastructure.AuditFilter("quote.reject"));
        quotes.MapPost("{id:int}/accept", async (int id, QuoteService s, CancellationToken ct) =>
            Results.Ok(await s.AcceptAsync(id, ct))).RequireAuthorization(CrmRoles.WritePolicy);

        // Fulfilment (Phase 8): ERP-owned shipment/invoice/payment status is read-only here; returns are CRM-owned.
        quotes.MapGet("{id:int}/fulfilment", async (int id, FulfilmentService s, CancellationToken ct) =>
            Results.Ok(await s.GetFulfilmentAsync(id, ct))).RequireAuthorization(CrmRoles.ReadPolicy);
        customers.MapGet("{id:int}/invoices", async (int id, FulfilmentService s, CancellationToken ct) =>
            Results.Ok(await s.ListInvoicesForCustomerAsync(id, ct))).RequireAuthorization(CrmRoles.ReadPolicy);
        quotes.MapPost("{id:int}/returns", async (int id, ReturnRequest r, FulfilmentService s, CancellationToken ct) =>
        {
            var v = await s.RequestReturnAsync(id, r, ct);
            return Results.Created($"/api/v1/returns/{v.Return.Id}", v);
        }).RequireAuthorization(CrmRoles.WritePolicy);
        quotes.MapGet("{id:int}/returns", async (int id, FulfilmentService s, CancellationToken ct) =>
            Results.Ok(await s.ListReturnsForQuoteAsync(id, ct))).RequireAuthorization(CrmRoles.ReadPolicy);
        v1.MapGet("/returns/{id:int}", async (int id, FulfilmentService s, CancellationToken ct) =>
            Results.Ok(await s.GetReturnAsync(id, ct))).RequireAuthorization(CrmRoles.ReadPolicy);

        v1.MapPost("/integration/reconcile", async (Crm.Api.Integration.IntegrationService s, ITenantContext t,
                int? olderThanMinutes, CancellationToken ct) =>
                Results.Ok(new { requeued = await s.ReconcileAsync(t.TenantId, TimeSpan.FromMinutes(olderThanMinutes ?? 15), ct) }))
            .RequireAuthorization(CrmRoles.AdminPolicy)
            .AddEndpointFilter(new Crm.Api.Infrastructure.AuditFilter("integration.reconcile"));

        v1.MapGet("/audit", async (int? take, Crm.Api.Infrastructure.AdminAudit a, CancellationToken ct) =>
            Results.Ok(await a.ListAsync(take ?? 50, ct))).RequireAuthorization(CrmRoles.AdminPolicy);

        v1.MapGet("/customers/{id:int}/integration", async (int id, CustomerService c,
                Crm.Api.Integration.IntegrationService s, ITenantContext t, CancellationToken ct) =>
            {
                await c.GetAsync(id, ct); // tenant check
                var m = await s.GetMappingAsync(t.TenantId, id, ct);
                return m is null ? Results.NotFound() : Results.Ok(m);
            }).RequireAuthorization(CrmRoles.ReadPolicy);

        v1.MapGet("/contacts/{id:int}", async (int id, ContactService s, CancellationToken ct) =>
            Results.Ok(await s.GetAsync(id, ct))).RequireAuthorization(CrmRoles.ReadPolicy);
    }
}
