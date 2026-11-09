using Foundgine.Extensions.Temporal.Contracts;

namespace Foundgine.Orchestration.Process;

/// <summary>
/// Server-side definition of the commercial-order saga on Foundgine's generic process workflow. Compensations run in
/// reverse step order, so mark-crm-failed (registered on the FIRST step) runs last, after the ERP undo steps.
/// </summary>
public static class CommercialOrderProcess
{
    public const string Name = "commercial-order-fulfillment";
    public const string Topic = "CommercialOrderAccepted";

    public static class Step
    {
        public const string Begin = "begin";
        public const string CreateErpOrder = "create-erp-order";
        public const string ReserveInventory = "reserve-inventory";
        public const string AssertReserved = "assert-reserved";
        public const string ConfirmCrm = "confirm-crm";
    }

    public static class Op
    {
        public const string Begin = "commercial-order.begin";
        public const string MarkCrmFailed = "commercial-order.mark-crm-failed";
        public const string CreateErpOrder = "commercial-order.create-erp-order";
        public const string CancelErpOrder = "commercial-order.cancel-erp-order";
        public const string ReserveInventory = "commercial-order.reserve-inventory";
        public const string ReleaseInventory = "commercial-order.release-inventory";
        public const string AssertReserved = "commercial-order.assert-reserved";
        public const string ConfirmCrm = "commercial-order.confirm-crm";
    }

    public static readonly FoundgineProcessDefinition Definition = new(Name,
    [
        new(Step.Begin, Op.Begin, CompensationOperation: Op.MarkCrmFailed),
        new(Step.CreateErpOrder, Op.CreateErpOrder, CompensationOperation: Op.CancelErpOrder),
        new(Step.ReserveInventory, Op.ReserveInventory, CompensationOperation: Op.ReleaseInventory),
        new(Step.AssertReserved, Op.AssertReserved),
        new(Step.ConfirmCrm, Op.ConfirmCrm),
    ]);

    /// <summary>Prefix of the failure reason recorded in CRM when ERP rejected the reservation (a business outcome).</summary>
    public const string RejectedPrefix = "REJECTED: ";
}
