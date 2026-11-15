using Crm.Api.Application;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace Crm.Api.Integration;

/// <summary>
/// Starts the Java <c>BotNegotiationWorkflow</c> (orchestration queue). The negotiation is advisory: neither bot writes to the
/// CRM or the ERP. Tenant and caller always come from the authenticated principal, never from the request body.
/// </summary>
public sealed class BotNegotiationStarter(Microsoft.Extensions.Options.IOptions<TemporalOptions> options)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ITemporalClient? _client;

    private async Task<ITemporalClient> ClientAsync()
    {
        if (_client is not null) return _client;
        await _gate.WaitAsync();
        try
        {
            return _client ??= await TemporalClient.ConnectAsync(new TemporalClientConnectOptions(options.Value.Address)
            {
                Namespace = options.Value.Namespace,
                Interceptors = [new Temporalio.Extensions.OpenTelemetry.TracingInterceptor()]
            });
        }
        finally { _gate.Release(); }
    }

    public static BotNegotiationStart Build(ITenantContext tenant, StartNegotiationRequest r, string correlationId)
    {
        var negotiationId = string.IsNullOrWhiteSpace(r.NegotiationId) ? Guid.NewGuid().ToString("N") : r.NegotiationId.Trim();
        var lines = r.Lines ?? [];
        if (lines.Count is < 1 or > 20) throw new ArgumentException("The brief needs between 1 and 20 lines.");
        foreach (var l in lines)
            if (string.IsNullOrWhiteSpace(l.Sku) || l.Quantity < 1 || l.MinQuantity < 1 || l.MinQuantity > l.Quantity || l.MaxUnitPrice < 0)
                throw new ArgumentException($"Invalid brief line for '{l.Sku}'.");
        var maxTurns = r.MaxTurns ?? 6;
        if (maxTurns is < 2 or > 10) throw new ArgumentException("maxTurns must be between 2 and 10.");
        return new BotNegotiationStart(1, tenant.TenantId, correlationId, negotiationId, r.CrmCustomerId, maxTurns,
            new Brief(r.Note, lines));
    }

    /// <summary>Idempotent per tenant + negotiationId: a repeat returns the workflow id of the one already running.</summary>
    public async Task<string> StartAsync(BotNegotiationStart start, string initiator, CancellationToken ct)
    {
        var client = await ClientAsync();
        var id = SalesBotNames.WorkflowId(start.TenantId, start.NegotiationId);
        try
        {
            await client.StartWorkflowAsync(SalesBotNames.Workflow, [start],
                new WorkflowOptions(id, options.Value.OrchestrationTaskQueue)
                {
                    IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.RejectDuplicate,
                    Memo = new Dictionary<string, object>
                    {
                        ["correlationId"] = start.CorrelationId, ["tenantId"] = start.TenantId, ["initiator"] = initiator
                    }
                });
        }
        catch (WorkflowAlreadyStartedException) { }
        return id;
    }

    /// <summary>Status and transcript of a negotiation of this tenant (workflow queries of the Java workflow).</summary>
    public async Task<object> GetAsync(string tenantId, string negotiationId)
    {
        var client = await ClientAsync();
        var handle = client.GetWorkflowHandle(SalesBotNames.WorkflowId(tenantId, negotiationId));
        var status = await handle.QueryAsync<string>("status", []);
        var transcript = await handle.QueryAsync<IReadOnlyList<BotMessage>>("transcript", []);
        return new { negotiationId, status, transcript };
    }
}
