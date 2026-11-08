using Crm.Api.Integration;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Crm.Worker.Workflows;

/// <summary>Business coordinator. Java owns ERP order/reservation; ERP outbox events drive CRM projections and completion.</summary>
[Workflow(CustomerToCashNames.Workflow)]
public sealed class CustomerToCashWorkflow
{
    private const int ApprovalTimeoutHours = 24;
    private string _status = "Started";
    private ApprovalDecision? _approval;
    private readonly List<object> _pending = [];
    private readonly Dictionary<string, RecordShipmentInput> _shipments = [];
    private readonly Dictionary<string, RecordInvoiceInput> _invoices = [];
    private readonly Dictionary<string, RecordPaymentInput> _payments = [];
    private string _erpOrderId = string.Empty;

    [WorkflowRun]
    public async Task<CustomerToCashResult> RunAsync(CustomerToCashStart input)
    {
        Validate(input);
        if (input.ApprovalRequired)
        {
            _status = "AwaitingApproval";
            var decided = await Workflow.WaitConditionAsync(() => _approval is not null, TimeSpan.FromHours(ApprovalTimeoutHours));
            if (!decided)
            {
                const string timeoutReason = "Approval was not received within 24 hours.";
                await ReportFailureToCrmAsync(input, timeoutReason);
                _status = "Expired";
                return new("Expired", string.Empty, timeoutReason, string.Empty);
            }
            if (!_approval!.Approved)
            {
                var reason = string.IsNullOrWhiteSpace(_approval.Reason)
                    ? $"Quote approval was rejected by {_approval.DecidedBy}." : _approval.Reason;
                await ReportFailureToCrmAsync(input, reason);
                _status = "Rejected";
                return new("Rejected", string.Empty, reason, string.Empty);
            }
        }

        var childOptions = new ChildWorkflowOptions
        {
            Id = CustomerToCashNames.JavaWorkflowId(input.TenantId, input.QuoteId),
            TaskQueue = CustomerToCashNames.JavaOrchestrationTaskQueue,
            ParentClosePolicy = ParentClosePolicy.Terminate,
            IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.RejectDuplicate
        };
        _status = "WaitingForErpOrderWorkflow";
        var child = await Workflow.StartChildWorkflowAsync(CustomerToCashNames.JavaQuoteToOrderWorkflow, [input.Quote], childOptions);
        QuoteToOrderResult erpResult;
        try { erpResult = await child.GetResultAsync<QuoteToOrderResult>(); }
        catch (ChildWorkflowFailureException ex)
        {
            var reason = $"Java ERP order workflow failed: {ex.Message}";
            await ReportFailureToCrmAsync(input, reason);
            _status = "Failed";
            return new("Failed", string.Empty, reason, child.Id);
        }
        if (!string.Equals(erpResult.Status, "Completed", StringComparison.Ordinal))
        {
            await ReportFailureToCrmAsync(input, erpResult.Reason);
            _status = "Failed";
            return new("Failed", string.Empty, erpResult.Reason, child.Id);
        }

        _erpOrderId = erpResult.ErpOrderId;
        _status = "AwaitingFulfilment";
        // Signals received while the Java child ran are retained in workflow history and drained here.
        while (!IsCashComplete())
        {
            await Workflow.WaitConditionAsync(() => _pending.Count > 0 || IsCashComplete());
            while (_pending.Count > 0)
            {
                var next = _pending[0];
                _pending.RemoveAt(0);
                await ApplyEventAsync(input, next);
            }
        }
        _status = "Completed";
        return new("Completed", _erpOrderId, string.Empty, child.Id);
    }

    [WorkflowSignal("approval")]
    public Task SubmitApprovalAsync(ApprovalDecision decision) { _approval ??= decision; return Task.CompletedTask; }

    [WorkflowSignal("shipment")]
    public Task RecordShipmentAsync(RecordShipmentInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.ErpShipmentId) && input.Version > 0) _pending.Add(input);
        return Task.CompletedTask;
    }

    [WorkflowSignal("invoice")]
    public Task RecordInvoiceAsync(RecordInvoiceInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.ErpInvoiceId) && input.Version > 0) _pending.Add(input);
        return Task.CompletedTask;
    }

    [WorkflowSignal("payment")]
    public Task RecordPaymentAsync(RecordPaymentInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.ErpPaymentId) && input.Version > 0) _pending.Add(input);
        return Task.CompletedTask;
    }

    [WorkflowQuery("status")]
    public string GetStatus() => _status;

    private async Task ApplyEventAsync(CustomerToCashStart start, object item)
    {
        switch (item)
        {
            case RecordShipmentInput shipment:
                ValidateEvent(start, shipment.TenantId, shipment.ErpOrderId);
                if (string.IsNullOrWhiteSpace(shipment.ErpShipmentId) || shipment.Version < 1 ||
                    shipment.Status is not ("Packed" or "Shipped" or "InTransit" or "Delivered" or "Cancelled"))
                    throw InvalidFulfilmentEvent("Shipment event fields or status are invalid.");
                if (_shipments.TryGetValue(shipment.ErpShipmentId, out var oldShipment) && oldShipment.Version >= shipment.Version) return;
                // CRM projection is applied exactly once by the Java ErpCrmProjectionWorkflow.
                // The C# parent consumes this signal only for its business completion gate.
                _shipments[shipment.ErpShipmentId] = shipment;
                break;
            case RecordInvoiceInput invoice:
                ValidateEvent(start, invoice.TenantId, invoice.ErpOrderId);
                if (string.IsNullOrWhiteSpace(invoice.ErpInvoiceId) || string.IsNullOrWhiteSpace(invoice.Number) ||
                    invoice.Version < 1 || invoice.Total < 0 || invoice.IssuedAt == default ||
                    invoice.Status is not ("Issued" or "PartiallyPaid" or "Paid" or "Overdue" or "Void"))
                    throw InvalidFulfilmentEvent("Invoice event fields or status are invalid.");
                if (_invoices.TryGetValue(invoice.ErpInvoiceId, out var oldInvoice) && oldInvoice.Version >= invoice.Version) return;
                // CRM projection is applied exactly once by the Java ErpCrmProjectionWorkflow.
                _invoices[invoice.ErpInvoiceId] = invoice;
                break;
            case RecordPaymentInput payment:
                ValidateEvent(start, payment.TenantId, payment.ErpOrderId);
                if (string.IsNullOrWhiteSpace(payment.ErpPaymentId) || string.IsNullOrWhiteSpace(payment.ErpInvoiceId) ||
                    payment.Version < 1 || payment.Amount < 0 ||
                    payment.Status is not ("Received" or "Reconciled" or "Reversed"))
                    throw InvalidFulfilmentEvent("Payment event fields or status are invalid.");
                if (_payments.TryGetValue(payment.ErpPaymentId, out var oldPayment) && oldPayment.Version >= payment.Version) return;
                // CRM projection is applied exactly once by the Java ErpCrmProjectionWorkflow.
                _payments[payment.ErpPaymentId] = payment;
                break;
            default:
                throw new ApplicationFailureException("Unsupported ERP fulfilment event.", errorType: "UnsupportedFulfilmentEvent", nonRetryable: true);
        }
    }

    private bool IsCashComplete()
    {
        if (_shipments.Count == 0 || _invoices.Count == 0 || _payments.Count == 0) return false;
        if (_shipments.Values.Any(x => x.Status != "Delivered") || _invoices.Values.Any(x => x.Status != "Paid")) return false;
        foreach (var invoice in _invoices.Values)
        {
            var reconciled = _payments.Values.Where(p => p.ErpInvoiceId == invoice.ErpInvoiceId)
                .Sum(p => p.Status == "Reconciled" ? p.Amount : 0m);
            if (reconciled < invoice.Total) return false;
        }
        return true;
    }

    private static ApplicationFailureException InvalidFulfilmentEvent(string message) =>
        new(message, errorType: "InvalidFulfilmentEvent", nonRetryable: true);

    private void ValidateEvent(CustomerToCashStart start, string tenantId, string erpOrderId)
    {
        if (!string.Equals(start.TenantId, tenantId, StringComparison.Ordinal) ||
            !string.Equals(_erpOrderId, erpOrderId, StringComparison.Ordinal))
            throw new ApplicationFailureException("ERP fulfilment event tenant/order identity is invalid.",
                errorType: "InvalidFulfilmentEvent", nonRetryable: true);
    }

    private static void Validate(CustomerToCashStart input)
    {
        if (input is null || input.Quote is null || input.SchemaVersion != 1 || input.Quote.SchemaVersion != 1)
            throw new ApplicationFailureException("Only customer-to-cash contract schema version 1 is supported.",
                errorType: "InvalidCustomerToCashContract", nonRetryable: true);
        if (string.IsNullOrWhiteSpace(input.TenantId) || input.TenantId != input.Quote.TenantId)
            throw new ApplicationFailureException("Tenant identity is missing or inconsistent across the workflow contract.",
                errorType: "InvalidCustomerToCashContract", nonRetryable: true);
        if (input.QuoteId != input.Quote.QuoteId || input.OpportunityId != input.Quote.OpportunityId ||
            input.CrmCustomerId != input.Quote.CrmCustomerId || input.CorrelationId != input.Quote.CorrelationId)
            throw new ApplicationFailureException("The parent and quote contract identifiers must match.",
                errorType: "InvalidCustomerToCashContract", nonRetryable: true);
    }

    private static Task ReportFailureToCrmAsync(CustomerToCashStart input, string reason) =>
        Workflow.ExecuteActivityAsync(CustomerToCashNames.CrmMarkOrderFailedActivity,
            [default], default);
            // [new MarkOrderFailedInput(input.TenantId, input.QuoteId, reason)], CrmActivityOptions());
}

public sealed record CustomerToCashResult(
    [property: System.Text.Json.Serialization.JsonPropertyName("status")] string Status,
    [property: System.Text.Json.Serialization.JsonPropertyName("erpOrderId")] string ErpOrderId,
    [property: System.Text.Json.Serialization.JsonPropertyName("reason")] string Reason,
    [property: System.Text.Json.Serialization.JsonPropertyName("javaWorkflowId")] string JavaWorkflowId);
