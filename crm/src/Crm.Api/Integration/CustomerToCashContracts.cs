using System.Text.Json.Serialization;

namespace Crm.Api.Integration;

/// <summary>Versioned cross-language start contract. Property names are pinned to the JSON schema.</summary>
public sealed record CustomerToCashStart(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("quoteId")] int QuoteId,
    [property: JsonPropertyName("opportunityId")] int OpportunityId,
    [property: JsonPropertyName("crmCustomerId")] int CrmCustomerId,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("approvalRequired")] bool ApprovalRequired,
    [property: JsonPropertyName("quote")] QuoteAccepted Quote);

/// <summary>Mirror of contracts/schemas/quote-accepted.v1.json and Java Contracts.QuoteAccepted.</summary>
public sealed record QuoteAccepted(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("quoteId")] int QuoteId,
    [property: JsonPropertyName("opportunityId")] int OpportunityId,
    [property: JsonPropertyName("crmCustomerId")] int CrmCustomerId,
    [property: JsonPropertyName("erpCustomerId")] string ErpCustomerId,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("total")] decimal Total,
    [property: JsonPropertyName("lines")] IReadOnlyList<QuoteLine> Lines);

public sealed record QuoteLine(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("unitPrice")] decimal UnitPrice);

/// <summary>Result returned by the Java QuoteToOrderWorkflow.</summary>
public sealed record QuoteToOrderResult(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("erpOrderId")] string ErpOrderId,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record ApprovalDecision(
    [property: JsonPropertyName("approved")] bool Approved,
    [property: JsonPropertyName("decidedBy")] string DecidedBy,
    [property: JsonPropertyName("reason")] string Reason);

public static class CustomerToCashNames
{
    public const string Workflow = "CustomerToCashWorkflow";
    public const string JavaQuoteToOrderWorkflow = "QuoteToOrderWorkflow";
    public const string CrmMarkOrderFailedActivity = "crm.mark-order-failed";
    public const string CustomerToCashTaskQueue = "crm-orchestration";
    public const string JavaOrchestrationTaskQueue = "orchestration";
    public const string CrmTaskQueue = "crm";

    public static string WorkflowId(string tenantId, int quoteId) => $"CustomerToCash:{tenantId}:{quoteId}";
    public static string JavaWorkflowId(string tenantId, int quoteId) => $"QuoteToOrder:{tenantId}:{quoteId}";
}
