package com.enterprise.orchestration;

import io.temporal.activity.ActivityOptions;
import io.temporal.common.RetryOptions;
import io.temporal.failure.ActivityFailure;
import io.temporal.failure.ApplicationFailure;
import io.temporal.workflow.ActivityStub;
import io.temporal.workflow.Workflow;
import java.time.Duration;

/**
 * 1. ERP authorizes or rejects the return (idempotent on tenant + CRM return id). A malformed request is treated as a
 *    rejection with the validation message, so the CRM return never stays Requested.
 * 2. CRM records the decision (Authorized with the ERP return id, or Rejected with the note).
 * 3. If authorized, wait for the goods-received signal (30 days). A signal that arrives early is remembered.
 * 4. ERP puts the stock back (once), CRM records Completed.
 * On timeout the return stays Authorized in both systems (the result says AwaitingGoodsExpired) for an operator to resolve.
 * CRM outages are waited out; a permanent CRM refusal fails the workflow visibly. Signals are not authenticated yet.
 */
public class ReturnProcessingWorkflowImpl implements ReturnProcessingWorkflow {
    static final Duration GOODS_TIMEOUT = Duration.ofDays(30);

    private static ActivityOptions options(String queue, int maxAttempts, String... doNotRetry) {
        return ActivityOptions.newBuilder().setTaskQueue(queue).setStartToCloseTimeout(Duration.ofSeconds(30))
                .setRetryOptions(RetryOptions.newBuilder()
                        .setInitialInterval(Duration.ofSeconds(1)).setBackoffCoefficient(2.0)
                        .setMaximumInterval(Duration.ofMinutes(1)).setMaximumAttempts(maxAttempts) // 0 = unlimited
                        .setDoNotRetry(doNotRetry).build())
                .build();
    }

    private final ErpReturnActivities erp = Workflow.newActivityStub(ErpReturnActivities.class,
            options(Contracts.ERP_QUEUE, 10, "ValidationFailed"));
    private final ActivityStub crm = Workflow.newUntypedActivityStub(
            options(Contracts.CRM_QUEUE, 0, "CrmNotFoundException", "CrmValidationException", "CrmConflictException"));
    private String status = "Started";
    private boolean received;

    @Override
    public Contracts.ReturnResult process(Contracts.ReturnRequested in) {
        status = "Authorizing";
        Contracts.ErpReturnDecision decision;
        try {
            decision = erp.authorizeReturn(in);
        } catch (ActivityFailure e) {
            return reject(in, "", reasonOf(e));
        }
        if ("Rejected".equals(decision.decision())) return reject(in, decision.erpReturnId(), decision.note());

        status = "RecordingDecision";
        crm.execute(Contracts.CRM_RECORD_RETURN_DECISION, Void.class,
                new Contracts.RecordReturnDecisionInput(in.tenantId(), in.returnId(), "Authorized", decision.erpReturnId(), ""));

        status = "AwaitingGoods";
        if (!Workflow.await(GOODS_TIMEOUT, () -> received)) {
            status = "AwaitingGoodsExpired";
            return new Contracts.ReturnResult("AwaitingGoodsExpired", decision.erpReturnId(), "Goods were not received in time.");
        }

        status = "Completing";
        erp.completeReturn(new Contracts.CompleteReturnRequest(in.tenantId(), decision.erpReturnId()));
        crm.execute(Contracts.CRM_RECORD_RETURN_DECISION, Void.class,
                new Contracts.RecordReturnDecisionInput(in.tenantId(), in.returnId(), "Completed", decision.erpReturnId(), ""));
        status = "Completed";
        return new Contracts.ReturnResult("Completed", decision.erpReturnId(), "");
    }

    private Contracts.ReturnResult reject(Contracts.ReturnRequested in, String erpReturnId, String note) {
        status = "RecordingDecision";
        crm.execute(Contracts.CRM_RECORD_RETURN_DECISION, Void.class,
                new Contracts.RecordReturnDecisionInput(in.tenantId(), in.returnId(), "Rejected", "", note));
        status = "Rejected";
        return new Contracts.ReturnResult("Rejected", erpReturnId, note);
    }

    @Override
    public void goodsReceived(Contracts.GoodsReceived r) { received = true; }

    @Override
    public String status() { return status; }

    private static String reasonOf(ActivityFailure e) {
        Throwable t = e;
        while (t.getCause() != null) t = t.getCause();
        var msg = t instanceof ApplicationFailure af ? af.getOriginalMessage() : t.getMessage();
        msg = msg == null ? e.getMessage() : msg;
        return msg.length() > 500 ? msg.substring(0, 500) : msg;
    }
}
