package com.enterprise.orchestration;

import io.temporal.workflow.QueryMethod;
import io.temporal.workflow.WorkflowInterface;
import io.temporal.workflow.WorkflowMethod;

/**
 * Operator path for a return whose goods-received wait expired (ReturnProcessingWorkflow ended with AwaitingGoodsExpired, so the
 * return is still Authorized in both systems). Started only by the ERP's authorized endpoint. Id: ReturnResolution:{tenant}:{returnId};
 * a failed run may be started again, a running or completed one may not.
 */
@WorkflowInterface
public interface ReturnResolutionWorkflow {
    @WorkflowMethod(name = "ReturnResolutionWorkflow")
    Contracts.ReturnResult resolve(Contracts.ResolveExpiredReturn input);

    /** Completing -> Completed. */
    @QueryMethod
    String status();
}
