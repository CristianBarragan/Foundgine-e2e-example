using Crm.Api.Api;
using Crm.Api.Application;
using Crm.Api.Domain;
using Crm.Api.Infrastructure;
using Crm.Api.Integration;
using Foundgine.Runtime;
using Foundgine.Extensions.Temporal.Http;
using Foundgine.Extensions.Temporal.Hosting;
using System.Threading.RateLimiting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Temporalio.Extensions.Hosting;
using Temporalio.Extensions.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFoundgine(options => options.UseCrmDomain());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<DatabaseMigrator>();
builder.Services.AddExceptionHandler<CrmExceptionHandler>();
builder.Services.AddProblemDetails();

// Identity: Foundgine Keycloak provider (see Infrastructure/IdentityExtensions.cs).
builder.AddCrmIdentity();

// Integration: outbox -> Temporal. Off unless Temporal:Enabled (the worker hosts the CRM activities).
builder.Services.Configure<TemporalOptions>(builder.Configuration.GetSection("Temporal"));
builder.Services.Configure<QuoteOptions>(builder.Configuration.GetSection("Quotes"));
builder.Services.AddScoped<IntegrationService>();
builder.Services.AddScoped<FulfilmentService>();
builder.Services.AddScoped<AdminAudit>();
builder.Services.AddScoped<Crm.Api.Application.Querying.CrmReader>();
builder.Services.AddScoped<InsightsService>();

// Rate limiting: fixed window per tenant (per client IP when unauthenticated). RateLimit:PermitsPerMinute, default 600.
var permits = builder.Configuration.GetValue("RateLimit:PermitsPerMinute", 600);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var key = ctx.User.FindFirst(HttpTenantContext.TenantClaim)?.Value is { Length: > 0 } t
            ? $"tenant:{t}"
            : $"ip:{ctx.Connection.RemoteIpAddress}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        });
    });
});
builder.Services.AddScoped<OutboxPublisher>();
builder.Services.AddScoped<IOutboxRouter, OutboxRouter>();
builder.Services.AddSingleton<IWorkflowStarter, TemporalWorkflowStarter>();
// Observability: OpenTelemetry traces + metrics over OTLP, only when Telemetry:Enabled. The Temporal TracingInterceptor
// carries the trace context in workflow/activity headers, so one trace spans CRM API -> outbox start -> workflow -> CRM activity.
var telemetryOn = builder.Configuration.GetValue<bool>("Telemetry:Enabled");
if (telemetryOn)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("crm-api"))
        .WithTracing(t => t
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource(TracingInterceptor.ClientSource.Name, TracingInterceptor.ActivitiesSource.Name)
            .AddOtlpExporter())
        .WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddMeter(Telemetry.MeterName)
            .AddOtlpExporter());
}

var temporal = builder.Configuration.GetSection("Temporal").Get<TemporalOptions>() ?? new TemporalOptions();
if (temporal.Enabled)
{
    // Temporal provider hooks: lazily-connected client, process catalog, starter, and the outbox bridge used by
    // OutboxRouter for topics in Foundgine:Temporal:TopicRoutes. The process workflow itself runs in Crm.Worker.
    builder.Services.AddFoundgineTemporal(o =>
        {
            o.Address = temporal.Address;
            o.Namespace = temporal.Namespace;
            o.TaskQueue = temporal.FoundgineTaskQueue;
            builder.Configuration.GetSection("Foundgine:Temporal").Bind(o);
        })
        .AddProcess(TenantUserProvisioning.Definition);
    // Foundgine's client (workflow start/status/signal from /api/v1/workflows and the outbox bridge) gets the same trace
    // interceptor as TemporalWorkflowStarter; Foundgine itself has no interceptor option, so this is a host-side override.
    builder.Services.AddFoundgineTemporalTracing();
    builder.Services.AddScoped<Foundgine.Extensions.Temporal.Messaging.FoundgineTemporalOutboxPublisher>();
    builder.Services.AddHostedService<OutboxPublisherService>();
    builder.Services.AddHostedTemporalWorker(temporal.Address, temporal.Namespace, temporal.CrmTaskQueue)
        .AddScopedActivities<CrmActivities>()
        .ConfigureOptions(o => o.Interceptors = [new TracingInterceptor()]); // trace context from workflow headers -> activity span
}

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStart"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(CancellationToken.None);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseRateLimiter(); // after authentication so the tenant claim is available
app.UseAuthorization();
app.MapCrmApi();
if (temporal.Enabled)
{
    // Start/status/decision/cancel for Foundgine processes. Tenant and subject come from the Keycloak warrant context,
    // so another tenant's workflow cannot be addressed. Admin-only: a process can create Keycloak users.
    app.MapGroup("/api/v1/workflows").RequireAuthorization(CrmRoles.AdminPolicy)
        .AddEndpointFilter(new AuditFilter("workflow"))
        .MapFoundgineTemporal("", o => o.ApprovalPolicy = CrmRoles.AdminPolicy);
}
app.Run();

public partial class Program;
