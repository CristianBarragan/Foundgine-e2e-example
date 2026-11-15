using System.Text.Json.Serialization;

namespace Crm.Api.Integration;

// Sales <-> fulfilment bot negotiation. Mirrors contracts/schemas/bot-*.v1.json and the Java records in
// temporal/workflows (Contracts.java). camelCase names are explicit so the Java worker and this worker share JSON only.

public static class SalesBotNames
{
    /// <summary>Activity type the Java BotNegotiationWorkflow calls on the "crm" queue.</summary>
    public const string TakeTurnActivity = "crm.sales-bot.take-turn";
    public const string Workflow = "BotNegotiationWorkflow";
    public const string Sales = "sales";
    public const string Fulfilment = "fulfilment";
    public static string WorkflowId(string tenantId, string negotiationId) => $"BotNegotiation:{tenantId}:{negotiationId}";
}

public sealed record BotLine(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("unitPrice")] decimal UnitPrice);

/// <summary>from: sales | fulfilment. intent: Propose | Counter | Accept | Decline.</summary>
public sealed record BotMessage(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("turn")] int Turn,
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("lines")] IReadOnlyList<BotLine> Lines,
    [property: JsonPropertyName("message")] string Message);

public sealed record BriefLine(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("minQuantity")] int MinQuantity,
    [property: JsonPropertyName("maxUnitPrice")] decimal MaxUnitPrice);

/// <summary>The customer's wishes and limits. Only the sales bot ever receives it.</summary>
public sealed record Brief(
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("lines")] IReadOnlyList<BriefLine> Lines);

public sealed record BotTurnInput(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("negotiationId")] string NegotiationId,
    [property: JsonPropertyName("turn")] int Turn,
    [property: JsonPropertyName("crmCustomerId")] int CrmCustomerId,
    [property: JsonPropertyName("brief")] Brief? Brief,
    [property: JsonPropertyName("history")] IReadOnlyList<BotMessage> History);

/// <summary>Input of BotNegotiationWorkflow (id BotNegotiation:{tenantId}:{negotiationId}).</summary>
public sealed record BotNegotiationStart(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("negotiationId")] string NegotiationId,
    [property: JsonPropertyName("crmCustomerId")] int CrmCustomerId,
    [property: JsonPropertyName("maxTurns")] int MaxTurns,
    [property: JsonPropertyName("brief")] Brief Brief);

/// <summary>HTTP body of POST /api/v1/negotiations (tenant and caller come from the token, never from the body).</summary>
public sealed record StartNegotiationRequest(
    string? NegotiationId, int CrmCustomerId, int? MaxTurns, string? Note, IReadOnlyList<BriefLine> Lines);
