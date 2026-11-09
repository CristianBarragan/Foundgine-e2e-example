using System.Security.Claims;
using System.Text.Json;
using Foundgine.Extensions.Temporal;
using Foundgine.Extensions.Temporal.Hosting;
using Foundgine.Orchestration.Process;
using Foundgine.Orchestration.Process.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);
var authority = Required(builder.Configuration["KEYCLOAK_AUTHORITY"], "KEYCLOAK_AUTHORITY");
var audience = Required(builder.Configuration["KEYCLOAK_AUDIENCE"], "KEYCLOAK_AUDIENCE");
var tenantClaimType = builder.Configuration["TENANT_CLAIM_TYPE"] ?? "tenant_id";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.Authority = authority;
    options.Audience = audience;
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
});
builder.Services.AddAuthorization();
builder.Services.AddFoundgineTemporal(o =>
    {
        o.Address = builder.Configuration["TEMPORAL_ADDRESS"] ?? "localhost:7233";
        o.Namespace = builder.Configuration["TEMPORAL_NAMESPACE"] ?? "default";
        o.TaskQueue = builder.Configuration["TEMPORAL_TASK_QUEUE"] ?? "foundgine-enterprise-orders";
        o.UseTls = builder.Configuration.GetValue<bool>("TEMPORAL_USE_TLS");
        o.ApiKey = builder.Configuration["TEMPORAL_API_KEY"];
    })
    .AddProcess(CommercialOrderProcess.Definition);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

// Manual (re)start path. The normal trigger is the outbox dispatcher. The business key is the CRM order id in "N"
// format, identical to the outbox message id, so both paths address the SAME workflow and a repeat is absorbed.
app.MapPost("/internal/workflows/commercial-orders/{orderId}/fulfillment",
    async (string orderId, CommercialOrderWorkflowInput input, ClaimsPrincipal principal,
           IFoundgineWorkflowStarter starter, CancellationToken ct) =>
    {
        var tenantId = principal.FindFirstValue(tenantClaimType);
        if (string.IsNullOrWhiteSpace(tenantId) || !string.Equals(tenantId, input.TenantId, StringComparison.Ordinal))
            return Results.Forbid();
        if (!string.Equals(orderId, input.CrmCommercialOrderId, StringComparison.Ordinal))
            return Results.BadRequest(new { error = "Route orderId must match CrmCommercialOrderId." });
        if (!Guid.TryParse(orderId, out var orderGuid))
            return Results.BadRequest(new { error = "orderId must be a GUID." });

        var started = await starter.StartAsync(new FoundgineWorkflowStartRequest(
            tenantId, $"api:{principal.FindFirstValue("sub") ?? "unknown"}", CommercialOrderProcess.Name,
            BusinessKey: orderGuid.ToString("N"),
            PayloadJson: JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web))), ct);
        return Results.Accepted($"/internal/workflows/{Uri.EscapeDataString(started.WorkflowId)}",
            new { workflowId = started.WorkflowId, alreadyExisted = started.AlreadyExisted });
    })
    .RequireAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", component = "foundgine-orchestration-api" }));
app.Run();

static string Required(string? value, string key) =>
    string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"Configuration value {key} is required.")
        : value;
