using Crm.Api.Integration;
using Crm.Worker.Workflows;
using Temporalio.Activities;
using Temporalio.Client;
using Temporalio.Exceptions;
using Temporalio.Testing;
using Temporalio.Worker;
using Temporalio.Workflows;
using Xunit;

namespace Crm.Worker.Tests;

/// <summary>Stands in for the Java QuoteToOrderWorkflow on the orchestration queue. The outcome is keyed off the quote.</summary>
[Workflow(CustomerToCashNames.JavaQuoteToOrderWorkflow)]
public sealed class FakeQuoteToOrderWorkflow
{
    public const string FailingCustomer = "ERP-FAIL";

    [WorkflowRun]
    public Task<QuoteToOrderResult> RunAsync(QuoteAccepted quote) =>
        Task.FromResult(quote.ErpCustomerId == FailingCustomer
            ? new QuoteToOrderResult("Failed", string.Empty, "Insufficient stock for SKU-1.")
            : new QuoteToOrderResult("Completed", "SO-5001", string.Empty));
}

/// <summary>The CRM-side activity the parent calls to report a failed order; can be told to refuse permanently.</summary>
public sealed class FakeCrmActivities
{
    public List<MarkOrderFailedInput> Calls { get; } = [];
    public string? RefuseWith { get; init; }

    [Activity(CustomerToCashNames.CrmMarkOrderFailedActivity)]
    public Task MarkOrderFailed(MarkOrderFailedInput input)
    {
        lock (Calls) Calls.Add(input);
        if (RefuseWith is not null)
            throw new ApplicationFailureException("refused", errorType: RefuseWith); // retryability comes from the workflow policy
        return Task.CompletedTask;
    }
}

public sealed class CustomerToCashWorkflowTests
{
    private const string Tenant = "acme";
    private const string ErpOrder = "SO-5001";

    private static CustomerToCashStart Start(int quoteId = 7, bool approval = false, string erpCustomer = "ERP-9001", int schema = 1)
    {
        var quote = new QuoteAccepted(1, Tenant, quoteId, 3, 42, erpCustomer, $"corr-{quoteId}", 200.00m,
            [new QuoteLine("SKU-1", "Widget", 2, 100.00m)]);
        return new CustomerToCashStart(schema, Tenant, quoteId, 3, 42, $"corr-{quoteId}", approval, quote);
    }

    private static RecordShipmentInput Shipment(string status, int version, string tenant = Tenant) =>
        new(tenant, ErpOrder, "SHP-0001", status, "NZ Post", "TRK-100", version);
    private static RecordInvoiceInput Invoice(string status, int version) =>
        new(Tenant, ErpOrder, "INV-0001", "2026-0001", 200.00m, status, new DateTime(2026, 11, 13, 1, 0, 0, DateTimeKind.Utc), null, version);
    private static RecordPaymentInput Payment(string status, int version) =>
        new(Tenant, ErpOrder, "INV-0001", "PAY-0001", 200.00m, status, version);

    /// <summary>Runs the C# workflow, the fake Java child and the fake CRM activities against a time-skipping server.</summary>
    private static async Task RunAsync(FakeCrmActivities crm, Func<WorkflowEnvironment, Task> body)
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
        using var parent = new TemporalWorker(env.Client,
            new TemporalWorkerOptions(CustomerToCashNames.CustomerToCashTaskQueue).AddWorkflow<CustomerToCashWorkflow>());
        using var java = new TemporalWorker(env.Client,
            new TemporalWorkerOptions(CustomerToCashNames.JavaOrchestrationTaskQueue).AddWorkflow<FakeQuoteToOrderWorkflow>());
        using var crmWorker = new TemporalWorker(env.Client,
            new TemporalWorkerOptions(CustomerToCashNames.CrmTaskQueue).AddAllActivities(crm));
        await parent.ExecuteAsync(() => java.ExecuteAsync(() => crmWorker.ExecuteAsync(() => body(env))));
    }

    private static Task<WorkflowHandle<CustomerToCashWorkflow, CustomerToCashResult>> StartAsync(WorkflowEnvironment env, CustomerToCashStart input) =>
        env.Client.StartWorkflowAsync(
            (CustomerToCashWorkflow wf) => wf.RunAsync(input),
            new WorkflowOptions(CustomerToCashNames.WorkflowId(input.TenantId, input.QuoteId), CustomerToCashNames.CustomerToCashTaskQueue));

    /// <summary>A workflow that would hang must fail the test, not stall the run.</summary>
    private static async Task<T> ResultAsync<T>(WorkflowHandle<CustomerToCashWorkflow, T> handle)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        return await handle.GetResultAsync(rpcOptions: new RpcOptions { CancellationToken = cts.Token });
    }

    private static async Task WaitForStatusAsync<T>(WorkflowHandle<CustomerToCashWorkflow, T> handle, string status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (await handle.QueryAsync(wf => wf.GetStatus()) != status)
        {
            Assert.True(DateTime.UtcNow < deadline, $"workflow never reached status {status}");
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task Full_journey_completes_when_everything_is_delivered_paid_and_reconciled()
    {
        var crm = new FakeCrmActivities();
        await RunAsync(crm, async env =>
        {
            var handle = await StartAsync(env, Start());
            await handle.SignalAsync(wf => wf.RecordShipmentAsync(Shipment("Packed", 1)));
            await handle.SignalAsync(wf => wf.RecordShipmentAsync(Shipment("Delivered", 2)));
            await handle.SignalAsync(wf => wf.RecordInvoiceAsync(Invoice("Issued", 1)));
            await handle.SignalAsync(wf => wf.RecordInvoiceAsync(Invoice("Paid", 2)));
            await handle.SignalAsync(wf => wf.RecordPaymentAsync(Payment("Reconciled", 1)));

            var result = await ResultAsync(handle);
            Assert.Equal("Completed", result.Status);
            Assert.Equal(ErpOrder, result.ErpOrderId);
            Assert.Equal(CustomerToCashNames.JavaWorkflowId(Tenant, 7), result.JavaWorkflowId);
            Assert.Empty(crm.Calls); // nothing failed, so nothing was reported to the CRM
        });
    }

    [Fact]
    public async Task Duplicate_and_stale_versions_are_ignored_so_the_newest_fact_wins()
    {
        await RunAsync(new FakeCrmActivities(), async env =>
        {
            var handle = await StartAsync(env, Start());
            await handle.SignalAsync(wf => wf.RecordShipmentAsync(Shipment("Delivered", 2)));
            await handle.SignalAsync(wf => wf.RecordShipmentAsync(Shipment("Packed", 1)));     // stale: must not regress to Packed
            await handle.SignalAsync(wf => wf.RecordShipmentAsync(Shipment("Delivered", 2)));  // duplicate
            await handle.SignalAsync(wf => wf.RecordInvoiceAsync(Invoice("Paid", 2)));
            await handle.SignalAsync(wf => wf.RecordInvoiceAsync(Invoice("Issued", 1)));       // stale
            await handle.SignalAsync(wf => wf.RecordPaymentAsync(Payment("Reconciled", 2)));
            // If a stale event had been applied the shipment/invoice would not be Delivered/Paid and this would hang.
            Assert.Equal("Completed", (await ResultAsync(handle)).Status);
        });
    }

    [Fact]
    public async Task Unreconciled_payment_does_not_complete_the_journey()
    {
        await RunAsync(new FakeCrmActivities(), async env =>
        {
            var handle = await StartAsync(env, Start());
            await handle.SignalAsync(wf => wf.RecordShipmentAsync(Shipment("Delivered", 1)));
            await handle.SignalAsync(wf => wf.RecordInvoiceAsync(Invoice("Paid", 1)));
            await handle.SignalAsync(wf => wf.RecordPaymentAsync(Payment("Received", 1))); // not yet reconciled
            await WaitForStatusAsync(handle, "AwaitingFulfilment"); // child done, signals drained
            Assert.Equal(WorkflowExecutionStatus.Running, (await handle.DescribeAsync()).Status);

            await handle.SignalAsync(wf => wf.RecordPaymentAsync(Payment("Reconciled", 2)));
            Assert.Equal("Completed", (await ResultAsync(handle)).Status);
        });
    }

    [Fact]
    public async Task Rejected_approval_reports_the_reason_to_the_CRM_and_never_starts_the_java_workflow()
    {
        var crm = new FakeCrmActivities();
        await RunAsync(crm, async env =>
        {
            var handle = await StartAsync(env, Start(approval: true));
            await handle.SignalAsync(wf => wf.SubmitApprovalAsync(new ApprovalDecision(false, "boss", "Over budget.")));
            var result = await ResultAsync(handle);
            Assert.Equal("Rejected", result.Status);
            Assert.Equal("Over budget.", result.Reason);
            Assert.Equal(string.Empty, result.JavaWorkflowId);
            Assert.Equal("Over budget.", Assert.Single(crm.Calls).Reason);
        });
    }

    [Fact]
    public async Task Approval_not_received_within_24_hours_expires()
    {
        var crm = new FakeCrmActivities();
        await RunAsync(crm, async env =>
        {
            var handle = await StartAsync(env, Start(approval: true));
            var result = await ResultAsync(handle); // the time-skipping server fast-forwards the 24 h timer
            Assert.Equal("Expired", result.Status);
            Assert.Single(crm.Calls);
        });
    }

    [Fact]
    public async Task Java_failure_is_reported_to_the_CRM_and_returned_as_a_business_result()
    {
        var crm = new FakeCrmActivities();
        await RunAsync(crm, async env =>
        {
            var handle = await StartAsync(env, Start(erpCustomer: FakeQuoteToOrderWorkflow.FailingCustomer));
            var result = await ResultAsync(handle);
            Assert.Equal("Failed", result.Status);
            Assert.Equal("Insufficient stock for SKU-1.", result.Reason);
            Assert.Equal("Insufficient stock for SKU-1.", Assert.Single(crm.Calls).Reason);
        });
    }

    [Theory]
    [InlineData("CrmConflictException")]
    [InlineData("CrmNotFoundException")]
    [InlineData("CrmValidationException")]
    public async Task A_permanent_CRM_refusal_does_not_hide_the_real_outcome_or_fail_the_parent(string refusal)
    {
        var crm = new FakeCrmActivities { RefuseWith = refusal };
        await RunAsync(crm, async env =>
        {
            var handle = await StartAsync(env, Start(erpCustomer: FakeQuoteToOrderWorkflow.FailingCustomer));
            var result = await ResultAsync(handle); // must return, not throw WorkflowFailedException
            Assert.Equal("Failed", result.Status);
            Assert.Single(crm.Calls); // non-retryable: exactly one attempt
        });
    }

    [Fact]
    public async Task Event_for_another_tenant_fails_the_workflow_with_a_non_retryable_error()
    {
        await RunAsync(new FakeCrmActivities(), async env =>
        {
            var handle = await StartAsync(env, Start());
            await handle.SignalAsync(wf => wf.RecordShipmentAsync(Shipment("Packed", 1, tenant: "other")));
            var ex = await Assert.ThrowsAsync<WorkflowFailedException>(() => ResultAsync(handle));
            var failure = Assert.IsType<ApplicationFailureException>(ex.InnerException);
            Assert.Equal("InvalidFulfilmentEvent", failure.ErrorType);
        });
    }

    [Fact]
    public async Task Unsupported_contract_version_is_rejected_up_front()
    {
        await RunAsync(new FakeCrmActivities(), async env =>
        {
            var handle = await StartAsync(env, Start(schema: 2));
            var ex = await Assert.ThrowsAsync<WorkflowFailedException>(() => ResultAsync(handle));
            Assert.Equal("InvalidCustomerToCashContract", Assert.IsType<ApplicationFailureException>(ex.InnerException).ErrorType);
        });
    }
}
