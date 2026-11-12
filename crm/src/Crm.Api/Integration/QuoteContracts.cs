using System.Text.Json.Serialization;

namespace Crm.Api.Integration;

public sealed record QuoteLineDto(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("unitPrice")] decimal UnitPrice);

/// <summary>Workflow input of QuoteToOrderWorkflow. Idempotency key = tenantId + quoteId.</summary>
public sealed record QuoteAcceptedDto(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("quoteId")] int QuoteId,
    [property: JsonPropertyName("opportunityId")] int OpportunityId,
    [property: JsonPropertyName("crmCustomerId")] int CrmCustomerId,
    [property: JsonPropertyName("erpCustomerId")] string ErpCustomerId,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("total")] decimal Total,
    [property: JsonPropertyName("lines")] IReadOnlyList<QuoteLineDto> Lines);

public sealed record RecordErpOrderInput(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("quoteId")] int QuoteId,
    [property: JsonPropertyName("erpOrderId")] string ErpOrderId);

public sealed record MarkOrderFailedInput(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("quoteId")] int QuoteId,
    [property: JsonPropertyName("reason")] string Reason);

public static class QuoteIntegrationNames
{
    public const string QuoteAccepted = "QuoteAccepted";
    public const string QuoteToOrderWorkflow = "QuoteToOrderWorkflow";
    public const string RecordErpOrderActivity = "crm.record-erp-order";
    public const string MarkOrderFailedActivity = "crm.mark-order-failed";
    public static string WorkflowId(string tenantId, int quoteId) => $"QuoteToOrder:{tenantId}:{quoteId}";
}
