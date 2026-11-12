using Crm.Api.Api;
using Crm.Api.Application;
using Crm.Api.Domain;
using Crm.Api.Infrastructure;
using Crm.Api.Integration;
using Foundgine.Runtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Temporalio.Extensions.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFoundgine(options => options.UseCrmDomain());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<DatabaseMigrator>();
builder.Services.AddExceptionHandler<CrmExceptionHandler>();
builder.Services.AddProblemDetails();

// Authentication: Keycloak JWT bearer. Issuer, audience and signature are validated against the realm's
// discovery document. The dev-header scheme is opt-in (Auth:DevHeaders) and used by tests/local work only.
const string Smart = "Smart";
var devHeaders = builder.Configuration.GetValue<bool>("Auth:DevHeaders");
var auth = builder.Services.AddAuthentication(o =>
{
    o.DefaultScheme = Smart;
    o.DefaultChallengeScheme = Smart;
});
auth.AddPolicyScheme(Smart, Smart, o => o.ForwardDefaultSelector = ctx =>
    devHeaders && ctx.Request.Headers.ContainsKey("X-User-Id")
        ? DevHeaderAuthenticationHandler.Scheme
        : JwtBearerDefaults.AuthenticationScheme);
auth.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
{
    var cfg = builder.Configuration.GetSection("Auth");
    o.Authority = cfg["Authority"];
    o.Audience = cfg["Audience"];
    o.RequireHttpsMetadata = cfg.GetValue("RequireHttpsMetadata", true);
    o.MapInboundClaims = false; // keep "sub", "roles", "tenant_id" as issued
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "sub",
        RoleClaimType = "roles"
    };
});
if (devHeaders)
    auth.AddScheme<AuthenticationSchemeOptions, DevHeaderAuthenticationHandler>(
        DevHeaderAuthenticationHandler.Scheme, null);
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(CrmRoles.ReadPolicy, p => p.RequireRole(CrmRoles.Read))
    .AddPolicy(CrmRoles.WritePolicy, p => p.RequireRole(CrmRoles.Write))
    .AddPolicy(CrmRoles.AdminPolicy, p => p.RequireRole(CrmRoles.Admin));

// Integration: outbox -> Temporal. Off unless Temporal:Enabled (the worker hosts the CRM activities).
builder.Services.Configure<TemporalOptions>(builder.Configuration.GetSection("Temporal"));
builder.Services.Configure<QuoteOptions>(builder.Configuration.GetSection("Quotes"));
builder.Services.AddScoped<IntegrationService>();
builder.Services.AddScoped<FulfilmentService>();
builder.Services.AddScoped<AdminAudit>();

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
app.Run();

public partial class Program;
