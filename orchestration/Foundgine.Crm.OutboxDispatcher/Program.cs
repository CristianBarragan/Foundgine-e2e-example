using Foundgine.Extensions.Temporal;
using Foundgine.Orchestration.Process;
using Foundgine.Providers.Outbox;
using Npgsql;

// CRM-owned dispatcher host: leases foundgine_outbox rows (Foundgine.Providers PostgresOutbox) and starts the
// commercial-order process through the Foundgine Temporal bridge. Retry, backoff and dead-lettering are the
// Foundgine dispatcher's; the outbox message id (== CRM order id) is the workflow business key, so at-least-once
// redelivery maps to the same workflow id.
var builder = Host.CreateApplicationBuilder(args);
var cs = builder.Configuration.GetConnectionString("Crm") ?? builder.Configuration["CRM_DATABASE_URL"]
    ?? throw new InvalidOperationException("ConnectionStrings:Crm is required.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(cs));
builder.Services.AddFoundgineGatewayPostgresOutbox();

builder.Services.AddFoundgineTemporal(o =>
    {
        o.Address = builder.Configuration["TEMPORAL_ADDRESS"] ?? "localhost:7233";
        o.Namespace = builder.Configuration["TEMPORAL_NAMESPACE"] ?? "default";
        o.TaskQueue = builder.Configuration["TEMPORAL_TASK_QUEUE"] ?? "foundgine-enterprise-orders";
        o.UseTls = builder.Configuration.GetValue<bool>("TEMPORAL_USE_TLS");
        o.ApiKey = builder.Configuration["TEMPORAL_API_KEY"];
        o.UnroutedTopicBehavior = UnroutedTopicBehavior.Fail;
    })
    .AddProcess(CommercialOrderProcess.Definition)
    .RouteTopic(CommercialOrderProcess.Topic, CommercialOrderProcess.Name)
    .DispatchOutboxToTemporal(d =>
    {
        d.BatchSize = 20;
        d.PollInterval = TimeSpan.FromSeconds(2);
        d.LeaseDuration = TimeSpan.FromSeconds(30);
    });

await builder.Build().RunAsync();
