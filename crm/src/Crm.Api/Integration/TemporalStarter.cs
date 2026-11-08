using Temporalio.Client;
using Temporalio.Exceptions;

namespace Crm.Api.Integration;

public sealed class TemporalOptions
{
    public bool Enabled { get; set; }
    public string Address { get; set; } = "localhost:7233";
    public string Namespace { get; set; } = "enterprise";
    public string OrchestrationTaskQueue { get; set; } = "orchestration";
    public string CustomerToCashTaskQueue { get; set; } = "crm-orchestration";
    public string CrmTaskQueue { get; set; } = "crm";
    /// <summary>Queue of the Foundgine process workflow (Keycloak admin operations); served by Crm.Worker.</summary>
    public string FoundgineTaskQueue { get; set; } = "foundgine";
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

    // Memo is visible in Temporal; it carries ids only (never tokens): who asked, for which tenant, under which correlation id.
    private static Dictionary<string, object> Memo(string tenantId, string correlationId, string? initiator) => new()
    {
        ["correlationId"] = correlationId, ["tenantId"] = tenantId, ["initiator"] = initiator ?? "system:outbox"
    };

    public async Task StartCustomerOnboardingAsync(CustomerSyncDto dto, CancellationToken ct, string? initiator = null)
    {
        var client = await ClientAsync();
        var id = IntegrationNames.WorkflowId(dto.TenantId, dto.CrmCustomerId, dto.Attempt);
        try
        {
            await client.StartWorkflowAsync(IntegrationNames.OnboardingWorkflow, [dto],
                new WorkflowOptions(id, options.Value.OrchestrationTaskQueue)
                {
                    IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.RejectDuplicate,
                    Memo = Memo(dto.TenantId, dto.CorrelationId, initiator)
                });
        }
        catch (WorkflowAlreadyStartedException)
        {
            // Duplicate delivery: the workflow for this business key already exists.
        }
    }

    /// <summary>Starts the C# customer-to-cash parent; it starts the Java QuoteToOrderWorkflow as a child.</summary>
    public async Task StartQuoteToOrderAsync(QuoteAcceptedDto dto, CancellationToken ct, string? initiator = null)
    {
        var client = await ClientAsync();
        var quote = new QuoteAccepted(
            dto.SchemaVersion, dto.TenantId, dto.QuoteId, dto.OpportunityId, dto.CrmCustomerId,
            dto.ErpCustomerId, dto.CorrelationId, dto.Total,
            dto.Lines.Select(line => new QuoteLine(line.Sku, line.Description, line.Quantity, line.UnitPrice)).ToArray());
        var start = new CustomerToCashStart(
            1, dto.TenantId, dto.QuoteId, dto.OpportunityId, dto.CrmCustomerId,
            dto.CorrelationId, false, quote);
        try
        {
            await client.StartWorkflowAsync(CustomerToCashNames.Workflow, [start],
                new WorkflowOptions(CustomerToCashNames.WorkflowId(dto.TenantId, dto.QuoteId), options.Value.CustomerToCashTaskQueue)
                {
                    IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.RejectDuplicate,
                    Memo = Memo(dto.TenantId, dto.CorrelationId, initiator)
                });
        }
        catch (WorkflowAlreadyStartedException)
        {
            // Duplicate outbox delivery: the customer-to-cash parent already owns this quote.
        }
    }

    public async Task StartReturnProcessingAsync(ReturnRequestedDto dto, CancellationToken ct, string? initiator = null)
    {
        var client = await ClientAsync();
        try
        {
            await client.StartWorkflowAsync(FulfilmentIntegrationNames.ReturnProcessingWorkflow, [dto],
                new WorkflowOptions(FulfilmentIntegrationNames.ReturnWorkflowId(dto.TenantId, dto.ReturnId), options.Value.OrchestrationTaskQueue)
                {
                    IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.RejectDuplicate,
                    Memo = Memo(dto.TenantId, dto.CorrelationId, initiator)
                });
        }
        catch (WorkflowAlreadyStartedException)
        {
        }
    }
}
