using Crm.Api.Integration;
using Temporalio.Client;
using Temporalio.Worker;

namespace Crm.Worker.Workflows;

/// <summary>Hosts the C# workflow type on the shared orchestration queue, independently of Foundgine processes.</summary>
public sealed class CustomerToCashTemporalWorker(IConfiguration configuration, ILogger<CustomerToCashTemporalWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var section = configuration.GetSection("Foundgine:Temporal");
        var address = section["Address"] ?? "localhost:7233";
        var temporalNamespace = section["Namespace"] ?? "enterprise";
        var taskQueue = section["CustomerToCashTaskQueue"] ?? CustomerToCashNames.CustomerToCashTaskQueue;

        var client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions(address)
        {
            Namespace = temporalNamespace
        });

        using var worker = new TemporalWorker(
            client,
            new TemporalWorkerOptions(taskQueue).AddWorkflow<CustomerToCashWorkflow>());

        logger.LogInformation(
            "Customer-to-cash Temporal worker starting: address={Address}, namespace={Namespace}, taskQueue={TaskQueue}",
            address, temporalNamespace, taskQueue);

        await worker.ExecuteAsync(stoppingToken);
    }
}
