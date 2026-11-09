using System.Net.Http.Json;
using System.Text.Json;
using Foundgine.Extensions.Temporal.Activities;
using Foundgine.Extensions.Temporal.Contracts;
using Foundgine.Orchestration.Process;
using Foundgine.Orchestration.Process.Contracts;
using static Foundgine.Orchestration.Process.CommercialOrderProcess;

namespace Foundgine.Orchestration.Worker;

/// <summary>
/// Anti-corruption adapters: Temporal orchestrates; CRM and ERP remain owners of their data and are reached only over
/// their /api/integration routes. Idempotency keys keep the historical `{tenant}:{order}:{action}` shape, so ERP/CRM
/// dedupe is unchanged by the move onto the generic process workflow.
/// </summary>
public abstract class CommercialOrderOperation(IHttpClientFactory http) : IFoundgineTemporalOperation
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public abstract string Name { get; }
    public abstract Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext context, CancellationToken cancellationToken);

    protected HttpClient Erp => http.CreateClient("erp");
    protected HttpClient Crm => http.CreateClient("crm");

    protected static CommercialOrderWorkflowInput Input(FoundgineOperationContext c) =>
        JsonSerializer.Deserialize<CommercialOrderWorkflowInput>(c.PayloadJson, Json)
            ?? throw new FoundgineNonRetryableOperationException("Workflow payload is empty.");

    protected static string Key(CommercialOrderWorkflowInput i, string action) =>
        $"{i.TenantId}:{i.CrmCommercialOrderId}:{action}";

    protected static T? Output<T>(FoundgineOperationContext c, string step) where T : class =>
        c.Outputs.TryGetValue(step, out var json) && !string.IsNullOrWhiteSpace(json)
            ? JsonSerializer.Deserialize<T>(json, Json) : null;

    protected static FoundgineOperationResult Result(object value) => new(JsonSerializer.Serialize(value, Json));

    protected async Task PostAsync(HttpClient client, string path, object body, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(path, body, Json, ct);
        Ensure(response, path);
    }

    protected async Task<TResponse> PostRequiredAsync<TResponse>(HttpClient client, string path, object body, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(path, body, Json, ct);
        Ensure(response, path);
        return await response.Content.ReadFromJsonAsync<TResponse>(Json, ct)
            ?? throw new InvalidOperationException($"Integration endpoint {path} returned an empty response.");
    }

    // 4xx (other than 408/429) is a deterministic contract or domain rejection: retrying the identical command cannot
    // succeed, so fail the step without retries. 5xx and network errors keep retrying under the activity policy.
    private static void Ensure(HttpResponseMessage response, string path)
    {
        if (response.IsSuccessStatusCode) return;
        var status = (int)response.StatusCode;
        var detail = $"Integration endpoint {path} returned HTTP {status}.";
        if (status is >= 400 and < 500 and not 408 and not 429)
            throw new FoundgineNonRetryableOperationException(detail);
        throw new HttpRequestException(detail);
    }
}

/// <summary>First step. Does no work; exists so mark-crm-failed is the LAST compensation to run.</summary>
public sealed class BeginOperation(IHttpClientFactory http) : CommercialOrderOperation(http)
{
    public override string Name => Op.Begin;
    public override Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var i = Input(c);
        if (string.IsNullOrWhiteSpace(i.TenantId) || string.IsNullOrWhiteSpace(i.CrmCommercialOrderId) || i.Lines.Count == 0)
            throw new FoundgineNonRetryableOperationException(
                "Tenant, CRM commercial order ID and at least one order line are required.");
        if (!string.Equals(i.TenantId, c.TenantId, StringComparison.Ordinal))
            throw new FoundgineNonRetryableOperationException("Payload tenant does not match the workflow tenant.");
        return Task.FromResult(new FoundgineOperationResult());
    }
}

public sealed class CreateErpOrderOperation(IHttpClientFactory http) : CommercialOrderOperation(http)
{
    public override string Name => Op.CreateErpOrder;
    public override async Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var i = Input(c);
        var order = await PostRequiredAsync<ErpOrderResult>(Erp, "/api/integration/orders",
            new ErpOrderCommand(i.TenantId, i.CrmCommercialOrderId, i.OrderNumber, i.CustomerId, i.Currency, i.Lines,
                Key(i, "create-erp-order")), ct);
        return Result(order);
    }
}

/// <summary>Insufficient stock is a business outcome (ERP answers 200 isReserved:false), returned as output, not thrown.</summary>
public sealed class ReserveInventoryOperation(IHttpClientFactory http) : CommercialOrderOperation(http)
{
    public override string Name => Op.ReserveInventory;
    public override async Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var i = Input(c);
        var erpOrder = Output<ErpOrderResult>(c, Step.CreateErpOrder)
            ?? throw new FoundgineNonRetryableOperationException("ERP order output is missing.");
        var reservation = await PostRequiredAsync<InventoryReservationResult>(Erp, "/api/integration/inventory/reservations",
            new InventoryReservationCommand(i.TenantId, i.CrmCommercialOrderId, erpOrder.ErpOrderId, i.Lines,
                Key(i, "reserve-inventory")), ct);
        return Result(reservation);
    }
}

/// <summary>
/// Turns a rejected reservation into a non-retryable step failure carrying the REJECTED prefix. The process then
/// compensates (cancel ERP order, record the rejection in CRM) exactly as the original saga's rejection path did.
/// </summary>
public sealed class AssertReservedOperation(IHttpClientFactory http) : CommercialOrderOperation(http)
{
    public override string Name => Op.AssertReserved;
    public override Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var r = Output<InventoryReservationResult>(c, Step.ReserveInventory);
        if (r is { IsReserved: true } && !string.IsNullOrWhiteSpace(r.ReservationId))
            return Task.FromResult(new FoundgineOperationResult());
        throw new FoundgineNonRetryableOperationException(
            RejectedPrefix + (r?.RejectionReason ?? "Inventory reservation was rejected."));
    }
}

public sealed class ConfirmCrmOperation(IHttpClientFactory http) : CommercialOrderOperation(http)
{
    public override string Name => Op.ConfirmCrm;
    public override async Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var i = Input(c);
        var reservation = Output<InventoryReservationResult>(c, Step.ReserveInventory);
        await PostAsync(Crm, $"/api/integration/commercial-orders/{Uri.EscapeDataString(i.CrmCommercialOrderId)}/fulfillment-confirmed",
            new CompensationCommand(i.TenantId, i.CrmCommercialOrderId, reservation?.ReservationId ?? "",
                "ERP order created and inventory reserved.", Key(i, "confirm-crm")), ct);
        return new FoundgineOperationResult();
    }
}

public sealed class ReleaseInventoryOperation(IHttpClientFactory http) : CommercialOrderOperation(http)
{
    public override string Name => Op.ReleaseInventory;
    public override async Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var i = Input(c);
        var r = Output<InventoryReservationResult>(c, Step.ReserveInventory);
        // A rejected reservation holds nothing to release.
        if (r is not { IsReserved: true } || string.IsNullOrWhiteSpace(r.ReservationId)) return new FoundgineOperationResult();
        await PostAsync(Erp, $"/api/integration/inventory/reservations/{Uri.EscapeDataString(r.ReservationId)}/release",
            new CompensationCommand(i.TenantId, i.CrmCommercialOrderId, r.ReservationId, "Workflow compensation",
                Key(i, "release-inventory")), ct);
        return new FoundgineOperationResult();
    }
}

public sealed class CancelErpOrderOperation(IHttpClientFactory http) : CommercialOrderOperation(http)
{
    public override string Name => Op.CancelErpOrder;
    public override async Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var i = Input(c);
        var o = Output<ErpOrderResult>(c, Step.CreateErpOrder);
        if (o is null) return new FoundgineOperationResult();
        await PostAsync(Erp, $"/api/integration/orders/{Uri.EscapeDataString(o.ErpOrderId)}/cancel",
            new CompensationCommand(i.TenantId, i.CrmCommercialOrderId, o.ErpOrderId, "Workflow compensation",
                Key(i, "cancel-erp-order")), ct);
        return new FoundgineOperationResult();
    }
}

/// <summary>
/// Last compensation. Records a rejected reservation as "REJECTED: reason" (business outcome) and anything else
/// as a technical failure, so CRM keeps the distinction the original saga's Rejected result carried.
/// </summary>
public sealed class MarkCrmFailedOperation(IHttpClientFactory http) : CommercialOrderOperation(http)
{
    public override string Name => Op.MarkCrmFailed;
    public override async Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var i = Input(c);
        var reservation = Output<InventoryReservationResult>(c, Step.ReserveInventory);
        var erpOrder = Output<ErpOrderResult>(c, Step.CreateErpOrder);
        var rejected = reservation is { IsReserved: false };
        var reason = rejected
            ? RejectedPrefix + (reservation!.RejectionReason ?? "Inventory reservation was rejected.")
            : "Workflow failure";
        var externalId = reservation?.ReservationId ?? erpOrder?.ErpOrderId ?? i.CrmCommercialOrderId;
        await PostAsync(Crm, $"/api/integration/commercial-orders/{Uri.EscapeDataString(i.CrmCommercialOrderId)}/fulfillment-failed",
            new CompensationCommand(i.TenantId, i.CrmCommercialOrderId, externalId, reason, Key(i, "mark-crm-failed")), ct);
        return new FoundgineOperationResult();
    }
}
