package com.enterprise.orchestration;

import io.temporal.failure.ApplicationFailure;
import io.temporal.workflow.ActivityStub;
import io.temporal.workflow.Workflow;

/**
 * Late receipt: the ERP puts the stock back (idempotent: an already completed return changes nothing) and the CRM records
 * Completed (the same decision again is a no-op). The same activities and retry rules as ReturnProcessingWorkflow: CRM outages
 * are waited out, a permanent refusal fails the workflow visibly. The operator's identity is carried in the input and written
 * to the CRM decision note, so Temporal history and the CRM both show who resolved it.
 */
public class ReturnResolutionWorkflowImpl implements ReturnResolutionWorkflow {
    private final ErpReturnActivities erp = Workflow.newActivityStub(ErpReturnActivities.class,
            ReturnProcessingWorkflowImpl.options(Contracts.ERP_QUEUE, 10, "ValidationFailed"));
    private final ActivityStub crm = Workflow.newUntypedActivityStub(
            ReturnProcessingWorkflowImpl.options(Contracts.CRM_QUEUE, 0, "CrmNotFoundException", "CrmValidationException", "CrmConflictException"));
    private String status = "Started";

    @Override
    public Contracts.ReturnResult resolve(Contracts.ResolveExpiredReturn in) {
        if (in == null || blank(in.tenantId()) || blank(in.erpReturnId()) || blank(in.resolvedBy()) || in.returnId() <= 0)
            throw ApplicationFailure.newNonRetryableFailure("tenantId, returnId, erpReturnId and resolvedBy are required.", "ValidationFailed");

        status = "Completing";
        erp.completeReturn(new Contracts.CompleteReturnRequest(in.tenantId(), in.erpReturnId()));
        crm.execute(Contracts.CRM_RECORD_RETURN_DECISION, Void.class, new Contracts.RecordReturnDecisionInput(
                in.tenantId(), in.returnId(), "Completed", in.erpReturnId(), "Goods received after the wait expired; confirmed by " + in.resolvedBy() + "."));
        status = "Completed";
        return new Contracts.ReturnResult("Completed", in.erpReturnId(), "");
    }

    @Override
    public String status() { return status; }

    private static boolean blank(String s) { return s == null || s.isBlank(); }
}
