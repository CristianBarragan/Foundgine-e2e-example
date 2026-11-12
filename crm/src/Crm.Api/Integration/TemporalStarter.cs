using Temporalio.Client;
using Temporalio.Exceptions;

namespace Crm.Api.Integration;

public sealed class TemporalOptions
{
    public bool Enabled { get; set; }
    public string Address { get; set; } = "localhost:7233";
    public string Namespace { get; set; } = "enterprise";
    public string OrchestrationTaskQueue { get; set; } = "orchestration";
    public string CrmTaskQueue { get; set; } = "crm";
}

public sealed class TemporalWorkflowStarter(Microsoft.Extensions.Options.IOptions<TemporalOptions> options)
    : IWorkflowStarter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ITemporalClient? _client;

    private async Task<ITemporalClient> ClientAsync()
    {
        if (_client is not null) return _client;
        await _gate.WaitAsync();
        try
        {
            return _client ??= await TemporalClient.ConnectAsync(
                new TemporalClientConnectOptions(options.Value.Address)
                {
                    Namespace = options.Value.Namespace,
                    // Injects the current trace context into workflow start headers (no-op when OpenTelemetry is off).
                    Interceptors = [new Temporalio.Extensions.OpenTelemetry.TracingInterceptor()]
                });
        }
        finally { _gate.Release(); }
    }

    public async Task StartCustomerOnboardingAsync(CustomerSyncDto dto, CancellationToken ct)
    {
        var client = await ClientAsync();
        var id = IntegrationNames.WorkflowId(dto.TenantId, dto.CrmCustomerId, dto.Attempt);
        try
        {
            await client.StartWorkflowAsync(IntegrationNames.OnboardingWorkflow, [dto],
                new WorkflowOptions(id, options.Value.OrchestrationTaskQueue)
                {
                    IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.RejectDuplicate,
                    Memo = new Dictionary<string, object> { ["correlationId"] = dto.CorrelationId }
                });
        }
        catch (WorkflowAlreadyStartedException)
        {
            // Duplicate delivery: the workflow for this business key already exists.
        }
    }

    public async Task StartQuoteToOrderAsync(QuoteAcceptedDto dto, CancellationToken ct)
    {
        var client = await ClientAsync();
        try
        {
            await client.StartWorkflowAsync(QuoteIntegrationNames.QuoteToOrderWorkflow, [dto],
                new WorkflowOptions(QuoteIntegrationNames.WorkflowId(dto.TenantId, dto.QuoteId), options.Value.OrchestrationTaskQueue)
                {
                    IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.RejectDuplicate,
                    Memo = new Dictionary<string, object> { ["correlationId"] = dto.CorrelationId }
                });
        }
        catch (WorkflowAlreadyStartedException)
        {
        }
    }

    public async Task StartReturnProcessingAsync(ReturnRequestedDto dto, CancellationToken ct)
    {
        var client = await ClientAsync();
        try
        {
            await client.StartWorkflowAsync(FulfilmentIntegrationNames.ReturnProcessingWorkflow, [dto],
                new WorkflowOptions(FulfilmentIntegrationNames.ReturnWorkflowId(dto.TenantId, dto.ReturnId), options.Value.OrchestrationTaskQueue)
                {
                    IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.RejectDuplicate,
                    Memo = new Dictionary<string, object> { ["correlationId"] = dto.CorrelationId }
                });
        }
        catch (WorkflowAlreadyStartedException)
        {
        }
    }
}
