using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Foundgine.Crm.Api;

/// <summary>Commercial-order lifecycle owned by the CRM. ERP outcomes arrive only via the integration callbacks.</summary>
public static class CommercialOrderStatus
{
    public const string Accepted = "Accepted";
    public const string FulfillmentConfirmed = "FulfillmentConfirmed";
    public const string FulfillmentFailed = "FulfillmentFailed";
}

public sealed record CommercialOrderLineInput(string ProductId, decimal Quantity, string UnitOfMeasure);

/// <summary>
/// Tenant is never part of the request: it comes from the validated token. CompanyId is the CRM
/// customer; ProductId is an ERP-owned product reference passed through unchanged (see
/// integration/adapters/README.md — a reconciliation table is a later increment).
/// </summary>
public sealed record CreateCommercialOrder(Guid CompanyId, string Currency, IReadOnlyList<CommercialOrderLineInput> Lines);

public sealed class CommercialOrder
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public string OrderNumber { get; set; } = "";
    public Guid CompanyId { get; set; }
    public string Currency { get; set; } = "";
    public string Status { get; set; } = CommercialOrderStatus.Accepted;
    public string? FailureReason { get; set; }
    public string? ErpReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public IReadOnlyList<CommercialOrderLineInput> Lines { get; set; } = [];
}

/// <summary>Body of the orchestration callbacks; shape matches the worker's <c>CompensationCommand</c>.</summary>
public sealed record FulfillmentCallback(
    string TenantId, string CrmCommercialOrderId, string ExternalId, string Reason, string IdempotencyKey);

/// <summary>Outbox payload. Property names/shape must stay identical to the orchestrator's CommercialOrderWorkflowInput.</summary>
public sealed record CommercialOrderAcceptedPayload(
    string TenantId, string CrmCommercialOrderId, string OrderNumber, string CustomerId, string Currency,
    IReadOnlyList<CommercialOrderAcceptedLine> Lines);

public sealed record CommercialOrderAcceptedLine(string ProductId, decimal Quantity, string UnitOfMeasure);

public sealed class CommercialOrderIdempotencyConflictException(string message) : Exception(message);

public static class CommercialOrderValidation
{
    public const int MaxLines = 100;
    public const decimal MaxQuantity = 1_000_000m;

    public static CreateCommercialOrder Normalize(CreateCommercialOrder input)
    {
        if (input.CompanyId == Guid.Empty) throw new CrmValidationException("CompanyId is required.");

        var currency = (input.Currency ?? "").Trim().ToUpperInvariant();
        if (currency.Length != 3 || !currency.All(c => c is >= 'A' and <= 'Z'))
            throw new CrmValidationException("Currency must be a 3-letter ISO 4217 code.");

        if (input.Lines is null || input.Lines.Count == 0)
            throw new CrmValidationException("At least one order line is required.");
        if (input.Lines.Count > MaxLines)
            throw new CrmValidationException($"An order may have at most {MaxLines} lines.");

        var lines = new List<CommercialOrderLineInput>(input.Lines.Count);
        foreach (var line in input.Lines)
        {
            var product = (line?.ProductId ?? "").Trim();
            var uom = (line?.UnitOfMeasure ?? "").Trim();
            if (product.Length is 0 or > 64) throw new CrmValidationException("ProductId is required (max 64 characters).");
            if (uom.Length is 0 or > 16) throw new CrmValidationException("UnitOfMeasure is required (max 16 characters).");
            var quantity = line!.Quantity;
            if (quantity <= 0 || quantity > MaxQuantity)
                throw new CrmValidationException($"Quantity must be greater than 0 and at most {MaxQuantity:0}.");
            if (decimal.Round(quantity, 3) != quantity)
                throw new CrmValidationException("Quantity supports at most 3 decimal places.");
            lines.Add(new CommercialOrderLineInput(product, quantity, uom));
        }

        return new CreateCommercialOrder(input.CompanyId, currency, lines);
    }

    public static string NormalizeIdempotencyKey(string? key)
    {
        var value = (key ?? "").Trim();
        if (value.Length is 0 or > 200 || value.Any(c => c < 0x21 || c > 0x7E))
            throw new CrmValidationException("Idempotency-Key header is required (1-200 printable ASCII characters, no spaces).");
        return value;
    }

    /// <summary>Order-insensitive SHA-256 of a normalized request; binds an Idempotency-Key to one request body.</summary>
    public static string Fingerprint(CreateCommercialOrder normalized)
    {
        var lines = normalized.Lines
            .Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.ProductId}\u001f{l.Quantity:0.###}\u001f{l.UnitOfMeasure}"))
            .OrderBy(x => x, StringComparer.Ordinal);
        var canonical = $"{normalized.CompanyId:N}\u001e{normalized.Currency}\u001e{string.Join('\u001e', lines)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static CommercialOrderAcceptedPayload BuildPayload(
        string tenantId, Guid orderId, string orderNumber, CreateCommercialOrder normalized) =>
        new(tenantId, orderId.ToString(), orderNumber, normalized.CompanyId.ToString(), normalized.Currency,
            normalized.Lines.Select(l => new CommercialOrderAcceptedLine(l.ProductId, l.Quantity, l.UnitOfMeasure)).ToList());

    public static string SerializePayload(CommercialOrderAcceptedPayload payload) =>
        JsonSerializer.Serialize(payload, CrmJsonContext.Default.CommercialOrderAcceptedPayload);

    public static FulfillmentCallback ValidateCallback(Guid routeOrderId, FulfillmentCallback body)
    {
        if (string.IsNullOrWhiteSpace(body.TenantId)) throw new CrmValidationException("TenantId is required.");
        if (!Guid.TryParse(body.CrmCommercialOrderId, out var bodyId) || bodyId != routeOrderId)
            throw new CrmValidationException("CrmCommercialOrderId must match the route order id.");
        var key = NormalizeIdempotencyKey(body.IdempotencyKey);
        var reason = (body.Reason ?? "").Trim();
        if (reason.Length > 1000) reason = reason[..1000];
        return body with { TenantId = body.TenantId.Trim(), IdempotencyKey = key, Reason = reason, ExternalId = (body.ExternalId ?? "").Trim() };
    }
}
