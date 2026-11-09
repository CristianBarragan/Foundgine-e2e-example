using Foundgine.Orchestration.Workflows.Contracts;
using Temporalio.Activities;

namespace Foundgine.Orchestration.Workflows.Contracts;

public interface ICommercialOrderActivities
{
    [Activity]
    Task<ErpOrderResult> CreateErpOrderAsync(ErpOrderCommand command);

    [Activity]
    Task<InventoryReservationResult> ReserveInventoryAsync(InventoryReservationCommand command);

    [Activity]
    Task ConfirmCrmCommercialOrderAsync(CompensationCommand command);

    [Activity]
    Task ReleaseInventoryAsync(CompensationCommand command);

    [Activity]
    Task CancelErpOrderAsync(CompensationCommand command);

    [Activity]
    Task MarkCrmCommercialOrderFailedAsync(CompensationCommand command);
}
