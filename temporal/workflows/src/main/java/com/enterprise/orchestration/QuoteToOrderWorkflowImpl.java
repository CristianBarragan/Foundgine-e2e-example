package com.enterprise.orchestration;

import io.temporal.activity.ActivityOptions;
import io.temporal.common.RetryOptions;
import io.temporal.failure.ActivityFailure;
import io.temporal.failure.ApplicationFailure;
import io.temporal.workflow.ActivityStub;
import io.temporal.workflow.Workflow;
import java.time.Duration;

/**
 * 1. ERP creates the order and reserves stock in ONE atomic ERP transaction (idempotent on tenant + quote).
 *    Validation / pricing / stock failures are not retried and leave nothing behind: the workflow ends Failed.
 * 2. CRM records the ERP order id on the quote (idempotent). CRM being down is waited out (unlimited retries);
 *    only a permanent CRM refusal (quote gone, invalid, or already ordered differently) reaches the catch.
 * 3. Compensation: on such a refusal the ERP order is cancelled and its reservation released, so an accepted quote
 *    never leaves an orphan order or locked stock. Compensation is idempotent and runs even if the workflow is cancelled.
 * 4. Whenever the workflow ends Failed, the CRM is told (crm.mark-order-failed) so the quote leaves Accepted and shows the
 *    reason. If the CRM refuses even that (quote gone / already ordered), the workflow still ends Failed with its reason.
 * No tokens or secrets pass through here.
 */
public class QuoteToOrderWorkflowImpl implements QuoteToOrderWorkflow {
    private static ActivityOptions options(String queue, int maxAttempts, String... doNotRetry) {
        return ActivityOptions.newBuilder().setTaskQueue(queue).setStartToCloseTimeout(Duration.ofSeconds(30))
                .setRetryOptions(RetryOptions.newBuilder()
                        .setInitialInterval(Duration.ofSeconds(1)).setBackoffCoefficient(2.0)
                        .setMaximumInterval(Duration.ofMinutes(1)).setMaximumAttempts(maxAttempts) // 0 = unlimited
                        .setDoNotRetry(doNotRetry).build())
                .build();
    }

    private final ErpOrderActivities erp = Workflow.newActivityStub(ErpOrderActivities.class,
            options(Contracts.ERP_QUEUE, 10, "ValidationFailed", "InsufficientStock"));
    private final ErpOrderActivities erpCompensation = Workflow.newActivityStub(ErpOrderActivities.class,
            options(Contracts.ERP_QUEUE, 0, "ValidationFailed"));
    private final ActivityStub crm = Workflow.newUntypedActivityStub(
            options(Contracts.CRM_QUEUE, 0, "CrmNotFoundException", "CrmValidationException", "CrmConflictException"));
    private String status = "Started";

    @Override
    public Contracts.QuoteToOrderResult place(Contracts.QuoteAccepted in) {
        Contracts.ErpOrderResult order;
        try {
            status = "CreatingErpOrder";
            order = erp.createOrderWithReservation(in);
        } catch (ActivityFailure e) {
            var reason = reasonOf(e);
            markFailed(in, reason);
            status = "Failed";
            return new Contracts.QuoteToOrderResult("Failed", "", reason);
        }

        try {
            status = "RecordingInCrm";
            crm.execute(Contracts.CRM_RECORD_ERP_ORDER, Void.class,
                    new Contracts.RecordErpOrderInput(in.tenantId(), in.quoteId(), order.erpOrderId()));
            status = "Completed";
            return new Contracts.QuoteToOrderResult("Completed", order.erpOrderId(), "");
        } catch (ActivityFailure e) {
            var reason = reasonOf(e);
            status = "Compensating";
            Workflow.newDetachedCancellationScope(() -> erpCompensation.cancelOrder(
                    new Contracts.CancelOrderRequest(in.tenantId(), in.quoteId(), order.erpOrderId(),
                            "CRM rejected the order: " + reason))).run();
            var full = "CRM rejected the order (ERP order cancelled): " + reason;
            markFailed(in, full);
            status = "Failed";
            return new Contracts.QuoteToOrderResult("Failed", "", full);
        }
    }

    @Override
    public String status() { return status; }

    /** Best effort and runs even if the workflow is cancelled; a permanent CRM refusal must not hide the real failure. */
    private void markFailed(Contracts.QuoteAccepted in, String reason) {
        status = "ReportingFailureToCrm";
        try {
            Workflow.newDetachedCancellationScope(() -> crm.execute(Contracts.CRM_MARK_ORDER_FAILED, Void.class,
                    new Contracts.MarkOrderFailedInput(in.tenantId(), in.quoteId(), reason))).run();
        } catch (ActivityFailure ignored) {
            // CrmNotFound / CrmConflict (e.g. the quote is already Ordered): nothing more to report.
        }
    }

    private static String reasonOf(ActivityFailure e) {
        Throwable t = e;
        while (t.getCause() != null) t = t.getCause();
        var msg = t instanceof ApplicationFailure af ? af.getOriginalMessage() : t.getMessage();
        msg = msg == null ? e.getMessage() : msg;
        return msg.length() > 500 ? msg.substring(0, 500) : msg;
    }
}
