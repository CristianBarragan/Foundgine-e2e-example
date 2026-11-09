namespace Foundgine.Orchestration.Workflows.Contracts;

public sealed record CommercialOrderWorkflowInput(
    string TenantId,
    string CrmCommercialOrderId,
    string OrderNumber,
    string CustomerId,
    string Currency,
    IReadOnlyList<CommercialOrderLine> Lines);

public sealed record CommercialOrderLine(string ProductId, decimal Quantity, string UnitOfMeasure);

public sealed record ErpOrderResult(string ErpOrderId, string Status);

public sealed record InventoryReservationResult(bool IsReserved, string? ReservationId, string? RejectionReason);

public sealed record CommercialOrderWorkflowResult(
    string Status,
    string? ErpOrderId,
    string? ReservationId,
    string? FailureReason);

public sealed record ErpOrderCommand(
    string TenantId,
    string CrmCommercialOrderId,
    string OrderNumber,
    string CustomerId,
    string Currency,
    IReadOnlyList<CommercialOrderLine> Lines,
    string IdempotencyKey);

public sealed record InventoryReservationCommand(
    string TenantId,
    string CrmCommercialOrderId,
    string ErpOrderId,
    IReadOnlyList<CommercialOrderLine> Lines,
    string IdempotencyKey);

public sealed record CompensationCommand(
    string TenantId,
    string CrmCommercialOrderId,
    string ExternalId,
    string Reason,
    string IdempotencyKey);
