using Crm.Api.Integration;
using Crm.Worker.Workflows;
using Foundgine.Extensions.Keycloak.Admin;
using Foundgine.Extensions.Keycloak.Hosting;
using Foundgine.Extensions.Temporal.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

// This is a dedicated process worker. Do not host it in Crm.Api: AddFoundgineTemporalWorker installs an
// activity-scoped security context and must not replace the HTTP request-scoped authority provider.
var builder = Host.CreateApplicationBuilder(args);

var temporalSection = builder.Configuration.GetSection("Foundgine:Temporal");
var keycloakSection = builder.Configuration.GetSection("Foundgine:Keycloak");

var address = temporalSection["Address"];
if (string.IsNullOrWhiteSpace(address))
    throw new InvalidOperationException("Foundgine:Temporal:Address is required.");
var taskQueue = temporalSection["TaskQueue"];
if (string.IsNullOrWhiteSpace(taskQueue))
    throw new InvalidOperationException("Foundgine:Temporal:TaskQueue is required.");

var keycloak = builder.Services
    .AddFoundgineKeycloakWarrants(o => keycloakSection.Bind(o))
    .UseForTemporalWorker();

// An ephemeral signing key invalidates trust across restarts and is strictly a local-development convenience.
if (keycloakSection.GetValue<bool>("EphemeralWarrantKey"))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("EphemeralWarrantKey is permitted only in Development.");
    keycloak.UseEphemeralWarrantKey();
}

// Process definitions are compiled into the worker. Callers select a registered process name; they cannot submit
// arbitrary operation names or change the tenant/initiator carried by the trusted Temporal security envelope.
builder.Services.AddFoundgineTemporalWorker(o =>
    {
        temporalSection.Bind(o);
        o.WorkflowIdPrefix = temporalSection["WorkflowIdPrefix"] ?? "crm";
    })
    .AddProcess(TenantUserProvisioning.Definition)
    .AddKeycloakAdminOperations();

// The Foundgine worker keeps its dedicated foundgine queue. The business workflow worker independently polls
// the shared orchestration queue, alongside the Java worker which owns QuoteToOrderWorkflow.
builder.Services.AddHostedService<CustomerToCashTemporalWorker>();

// Worker lifecycle, Temporal reconnect/retry and shutdown are owned by the hosted services.
await builder.Build().RunAsync();
