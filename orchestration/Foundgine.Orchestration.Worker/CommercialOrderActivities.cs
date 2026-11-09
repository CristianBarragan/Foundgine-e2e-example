using System.Net.Http.Json;
using Foundgine.Orchestration.Workflows.Contracts;
using Temporalio.Activities;

namespace Foundgine.Orchestration.Worker;

/// <summary>
/// Anti-corruption adapter: Temporal orchestrates; CRM and ERP remain owners of their data.
/// The /api/integration routes are explicit adapter contracts to implement in each domain.
/// </summary>
public sealed class CommercialOrderActivities(HttpClient crm, HttpClient erp) : ICommercialOrderActivities
{
    [Activity]
    public async Task<ErpOrderResult> CreateErpOrderAsync(ErpOrderCommand command) =>
        await PostRequiredAsync<ErpOrderCommand, ErpOrderResult>(erp, "/api/integration/orders", command);

    [Activity]
    public async Task<InventoryReservationResult> ReserveInventoryAsync(InventoryReservationCommand command) =>
        await PostRequiredAsync<InventoryReservationCommand, InventoryReservationResult>(erp, "/api/integration/inventory/reservations", command);

    [Activity]
    public async Task ConfirmCrmCommercialOrderAsync(CompensationCommand command) =>
        await PostAsync(crm, $"/api/integration/commercial-orders/{Uri.EscapeDataString(command.CrmCommercialOrderId)}/fulfillment-confirmed", command);

    [Activity]
    public async Task ReleaseInventoryAsync(CompensationCommand command) =>
        await PostAsync(erp, $"/api/integration/inventory/reservations/{Uri.EscapeDataString(command.ExternalId)}/release", command);

    [Activity]
    public async Task CancelErpOrderAsync(CompensationCommand command) =>
        await PostAsync(erp, $"/api/integration/orders/{Uri.EscapeDataString(command.ExternalId)}/cancel", command);

    [Activity]
    public async Task MarkCrmCommercialOrderFailedAsync(CompensationCommand command) =>
        await PostAsync(crm, $"/api/integration/commercial-orders/{Uri.EscapeDataString(command.CrmCommercialOrderId)}/fulfillment-failed", command);

    private static async Task PostAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        Ensure(response, path);
    }

    // A 4xx (other than timeout/throttle) is a deterministic contract or domain rejection:
    // retrying the identical command cannot succeed, so fail the activity without retries and let the
    // workflow compensate / stay visible for operators. 5xx and network errors keep retrying.
    private static void Ensure(HttpResponseMessage response, string path)
    {
        if (response.IsSuccessStatusCode) return;
        var status = (int)response.StatusCode;
        var detail = $"Integration endpoint {path} returned HTTP {status}.";
        if (status is >= 400 and < 500 and not 408 and not 429)
            throw new Temporalio.Exceptions.ApplicationFailureException(detail, nonRetryable: true);
        throw new HttpRequestException(detail);
    }

    private static async Task<TResponse> PostRequiredAsync<TRequest, TResponse>(HttpClient client, string path, TRequest body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        Ensure(response, path);
        return await response.Content.ReadFromJsonAsync<TResponse>()
            ?? throw new InvalidOperationException($"Integration endpoint {path} returned an empty response.");
    }
}
