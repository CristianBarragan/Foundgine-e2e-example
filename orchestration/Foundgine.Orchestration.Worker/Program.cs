using Foundgine.Extensions.Keycloak;
using Foundgine.Extensions.Temporal;
using Foundgine.Orchestration.Process;
using Foundgine.Orchestration.Worker;

var builder = Host.CreateApplicationBuilder(args);
var erpBaseUrl = Required(builder.Configuration["ERP_BASE_URL"], "ERP_BASE_URL");
var crmBaseUrl = Required(builder.Configuration["CRM_BASE_URL"], "CRM_BASE_URL");
var serviceToken = builder.Configuration["SERVICE_TO_SERVICE_TOKEN"];

void ConfigureClient(HttpClient client, string baseUrl)
{
    client.BaseAddress = new Uri(baseUrl);
    if (!string.IsNullOrWhiteSpace(serviceToken))
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceToken);
}
builder.Services.AddHttpClient("erp", c => ConfigureClient(c, erpBaseUrl));
builder.Services.AddHttpClient("crm", c => ConfigureClient(c, crmBaseUrl));

// Keycloak-backed authority for activities (warrants for WorkerRoles, bounded to the workflow's tenant).
// Operations here call CRM/ERP over HTTP, but the Temporal worker requires a security-context factory regardless.
builder.Services.AddFoundgineKeycloakWarrants(o => builder.Configuration.GetSection("Foundgine:Keycloak").Bind(o))
    .UseForTemporalWorker();

builder.Services.AddFoundgineTemporalWorker(o =>
    {
        o.Address = builder.Configuration["TEMPORAL_ADDRESS"] ?? "localhost:7233";
        o.Namespace = builder.Configuration["TEMPORAL_NAMESPACE"] ?? "default";
        o.TaskQueue = builder.Configuration["TEMPORAL_TASK_QUEUE"] ?? "foundgine-enterprise-orders";
        o.UseTls = builder.Configuration.GetValue<bool>("TEMPORAL_USE_TLS");
        o.ApiKey = builder.Configuration["TEMPORAL_API_KEY"];
    })
    .AddProcess(CommercialOrderProcess.Definition)
    .AddOperation<BeginOperation>()
    .AddOperation<CreateErpOrderOperation>()
    .AddOperation<ReserveInventoryOperation>()
    .AddOperation<AssertReservedOperation>()
    .AddOperation<ConfirmCrmOperation>()
    .AddOperation<ReleaseInventoryOperation>()
    .AddOperation<CancelErpOrderOperation>()
    .AddOperation<MarkCrmFailedOperation>();

// RunAsync, not Run (https://github.com/temporalio/sdk-dotnet/issues/220).
await builder.Build().RunAsync();

static string Required(string? value, string key) =>
    string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"Configuration value {key} is required.")
        : value;
