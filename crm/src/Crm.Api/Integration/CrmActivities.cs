using Temporalio.Activities;

namespace Crm.Api.Integration;

/// <summary>CRM-owned Temporal activities. Inputs carry the tenant explicitly; no tokens ever enter workflow history.</summary>
public sealed class CrmActivities(IntegrationService integration, Application.QuoteService quotes,
    Application.FulfilmentService fulfilment)
{
    [Activity(IntegrationNames.GetCustomerActivity)]
    public Task<CustomerSyncDto> GetCustomerForSync(CustomerRef r) =>
        integration.GetCustomerForSyncAsync(r, ActivityExecutionContext.Current.CancellationToken);

    [Activity(IntegrationNames.RecordErpCustomerActivity)]
    public Task RecordErpCustomer(RecordErpCustomerInput i) =>
        integration.RecordErpCustomerAsync(i, ActivityExecutionContext.Current.CancellationToken);

    [Activity(IntegrationNames.MarkSyncFailedActivity)]
    public Task MarkSyncFailed(MarkSyncFailedInput i) =>
        integration.MarkSyncFailedAsync(i, ActivityExecutionContext.Current.CancellationToken);

    [Activity(QuoteIntegrationNames.RecordErpOrderActivity)]
    public Task RecordErpOrder(RecordErpOrderInput i) =>
        quotes.RecordErpOrderAsync(i, ActivityExecutionContext.Current.CancellationToken);

    [Activity(QuoteIntegrationNames.MarkOrderFailedActivity)]
    public Task MarkOrderFailed(MarkOrderFailedInput i) =>
        quotes.MarkOrderFailedAsync(i, ActivityExecutionContext.Current.CancellationToken);

    [Activity(FulfilmentIntegrationNames.RecordShipmentActivity)]
    public Task RecordShipment(RecordShipmentInput i) =>
        fulfilment.RecordShipmentAsync(i, ActivityExecutionContext.Current.CancellationToken);

    [Activity(FulfilmentIntegrationNames.RecordInvoiceActivity)]
    public Task RecordInvoice(RecordInvoiceInput i) =>
        fulfilment.RecordInvoiceAsync(i, ActivityExecutionContext.Current.CancellationToken);

    [Activity(FulfilmentIntegrationNames.RecordPaymentActivity)]
    public Task RecordPayment(RecordPaymentInput i) =>
        fulfilment.RecordPaymentAsync(i, ActivityExecutionContext.Current.CancellationToken);

    [Activity(FulfilmentIntegrationNames.RecordReturnDecisionActivity)]
    public Task RecordReturnDecision(RecordReturnDecisionInput i) =>
        fulfilment.RecordReturnDecisionAsync(i, ActivityExecutionContext.Current.CancellationToken);
}
