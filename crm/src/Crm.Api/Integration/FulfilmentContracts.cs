using System.Text.Json.Serialization;

namespace Crm.Api.Integration;

// Phase 8 contracts (see contracts/schemas). ERP-owned facts arrive as activity inputs keyed by ERP ids; "version"
// is a per-entity monotonically increasing number assigned by the ERP, so the CRM can ignore stale deliveries.

public sealed record RecordShipmentInput(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("erpOrderId")] string ErpOrderId,
    [property: JsonPropertyName("erpShipmentId")] string ErpShipmentId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("carrier")] string? Carrier,
    [property: JsonPropertyName("trackingNumber")] string? TrackingNumber,
    [property: JsonPropertyName("version")] int Version);

public sealed record RecordInvoiceInput(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("erpOrderId")] string ErpOrderId,
    [property: JsonPropertyName("erpInvoiceId")] string ErpInvoiceId,
    [property: JsonPropertyName("number")] string Number,
    [property: JsonPropertyName("total")] decimal Total,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("issuedAt")] DateTime IssuedAt,
    [property: JsonPropertyName("dueAt")] DateTime? DueAt,
    [property: JsonPropertyName("version")] int Version);

public sealed record RecordPaymentInput(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("erpOrderId")] string ErpOrderId,
    [property: JsonPropertyName("erpInvoiceId")] string ErpInvoiceId,
    [property: JsonPropertyName("erpPaymentId")] string ErpPaymentId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("version")] int Version);

public sealed record ReturnLineDto(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("quantity")] int Quantity);

/// <summary>Workflow input of ReturnProcessingWorkflow. Idempotency key = tenantId + returnId.</summary>
public sealed record ReturnRequestedDto(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("returnId")] int ReturnId,
    [property: JsonPropertyName("quoteId")] int QuoteId,
    [property: JsonPropertyName("crmCustomerId")] int CrmCustomerId,
    [property: JsonPropertyName("erpCustomerId")] string ErpCustomerId,
    [property: JsonPropertyName("erpOrderId")] string ErpOrderId,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("lines")] IReadOnlyList<ReturnLineDto> Lines);

/// <summary>Decision is Authorized, Rejected or Completed. Authorized needs an erpReturnId.</summary>
public sealed record RecordReturnDecisionInput(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("returnId")] int ReturnId,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("erpReturnId")] string? ErpReturnId,
    [property: JsonPropertyName("note")] string? Note);

public static class FulfilmentIntegrationNames
{
    public const string ReturnRequested = "ReturnRequested";
    public const string ReturnProcessingWorkflow = "ReturnProcessingWorkflow";
    public const string RecordShipmentActivity = "crm.record-shipment";
    public const string RecordInvoiceActivity = "crm.record-invoice";
    public const string RecordPaymentActivity = "crm.record-payment";
    public const string RecordReturnDecisionActivity = "crm.record-return-decision";
    public static string ReturnWorkflowId(string tenantId, int returnId) => $"ReturnProcessing:{tenantId}:{returnId}";
}
