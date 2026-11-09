using Foundgine.Orchestration.Workflows.Contracts;
using Temporalio.Common;
using Temporalio.Workflows;

namespace Foundgine.Orchestration.Workflows.Workflows;

/// <summary>
/// Durable cross-bounded-context coordinator. All I/O belongs in Activities;
/// this workflow contains deterministic sequencing and compensation only.
/// </summary>
[Workflow]
public sealed class CommercialOrderFulfillmentWorkflow
{
    private static readonly ActivityOptions ForwardOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(2),
        ScheduleToCloseTimeout = TimeSpan.FromMinutes(20),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(2),
            BackoffCoefficient = 2,
            MaximumInterval = TimeSpan.FromMinutes(1),
            MaximumAttempts = 8,
        },
    };

    private static readonly ActivityOptions CompensationOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(2),
        CancellationToken = CancellationToken.None, // Run compensation even after Workflow cancellation.
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(5),
            BackoffCoefficient = 2,
            MaximumInterval = TimeSpan.FromMinutes(5),
            MaximumAttempts = 0, // Unlimited attempts; keep the Workflow open for operational recovery.
        },
    };

    [WorkflowRun]
    public async Task<CommercialOrderWorkflowResult> RunAsync(CommercialOrderWorkflowInput input)
    {
        if (string.IsNullOrWhiteSpace(input.TenantId) ||
            string.IsNullOrWhiteSpace(input.CrmCommercialOrderId) ||
            input.Lines.Count == 0)
        {
            throw new ArgumentException("Tenant, CRM commercial order ID and at least one order line are required.");
        }

        var idempotencyRoot = $"{input.TenantId}:{input.CrmCommercialOrderId}";
        ErpOrderResult? erpOrder = null;
        InventoryReservationResult? reservation = null;

        try
        {
            erpOrder = await Workflow.ExecuteActivityAsync((ICommercialOrderActivities a) => a.CreateErpOrderAsync(new ErpOrderCommand(
                input.TenantId, input.CrmCommercialOrderId, input.OrderNumber,
                input.CustomerId, input.Currency, input.Lines,
                $"{idempotencyRoot}:create-erp-order")), ForwardOptions);

            reservation = await Workflow.ExecuteActivityAsync((ICommercialOrderActivities a) => a.ReserveInventoryAsync(new InventoryReservationCommand(
                input.TenantId, input.CrmCommercialOrderId, erpOrder.ErpOrderId,
                input.Lines, $"{idempotencyRoot}:reserve-inventory")), ForwardOptions);

            if (!reservation.IsReserved || string.IsNullOrWhiteSpace(reservation.ReservationId))
            {
                var reason = reservation.RejectionReason ?? "Inventory reservation was rejected.";
                await Workflow.ExecuteActivityAsync((ICommercialOrderActivities a) => a.CancelErpOrderAsync(new CompensationCommand(
                    input.TenantId, input.CrmCommercialOrderId, erpOrder.ErpOrderId,
                    reason, $"{idempotencyRoot}:cancel-erp-order")), CompensationOptions);
                await Workflow.ExecuteActivityAsync((ICommercialOrderActivities a) => a.MarkCrmCommercialOrderFailedAsync(new CompensationCommand(
                    input.TenantId, input.CrmCommercialOrderId, erpOrder.ErpOrderId,
                    reason, $"{idempotencyRoot}:mark-crm-failed")), CompensationOptions);

                return new("Rejected", erpOrder.ErpOrderId, null, reason);
            }

            await Workflow.ExecuteActivityAsync((ICommercialOrderActivities a) => a.ConfirmCrmCommercialOrderAsync(new CompensationCommand(
                input.TenantId, input.CrmCommercialOrderId, reservation.ReservationId,
                "ERP order created and inventory reserved.", $"{idempotencyRoot}:confirm-crm")), ForwardOptions);

            return new("Fulfilled", erpOrder.ErpOrderId, reservation.ReservationId, null);
        }
        catch (Exception exception)
        {
            var failureReason = $"Workflow failure: {exception.GetType().Name}";
            // Compensate in reverse order. If compensation itself cannot complete,
            // Temporal keeps retrying it and the Workflow remains visible for operations.
            if (reservation?.IsReserved == true && !string.IsNullOrWhiteSpace(reservation.ReservationId))
            {
                await Workflow.ExecuteActivityAsync((ICommercialOrderActivities a) => a.ReleaseInventoryAsync(new CompensationCommand(
                    input.TenantId, input.CrmCommercialOrderId, reservation.ReservationId,
                    failureReason, $"{idempotencyRoot}:release-inventory")), CompensationOptions);
            }

            if (erpOrder is not null)
            {
                await Workflow.ExecuteActivityAsync((ICommercialOrderActivities a) => a.CancelErpOrderAsync(new CompensationCommand(
                    input.TenantId, input.CrmCommercialOrderId, erpOrder.ErpOrderId,
                    failureReason, $"{idempotencyRoot}:cancel-erp-order")), CompensationOptions);
            }

            await Workflow.ExecuteActivityAsync((ICommercialOrderActivities a) => a.MarkCrmCommercialOrderFailedAsync(new CompensationCommand(
                input.TenantId, input.CrmCommercialOrderId,
                reservation?.ReservationId ?? erpOrder?.ErpOrderId ?? input.CrmCommercialOrderId,
                failureReason, $"{idempotencyRoot}:mark-crm-failed")), CompensationOptions);

            throw;
        }
    }
}
