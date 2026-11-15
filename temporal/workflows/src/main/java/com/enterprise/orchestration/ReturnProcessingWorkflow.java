package com.enterprise.orchestration;

import io.temporal.workflow.QueryMethod;
import io.temporal.workflow.SignalMethod;
import io.temporal.workflow.WorkflowInterface;
import io.temporal.workflow.WorkflowMethod;

/** Name "ReturnProcessingWorkflow" is what the CRM outbox publisher starts. Id: ReturnProcessing:{tenant}:{returnId}. */
@WorkflowInterface
public interface ReturnProcessingWorkflow {
    @WorkflowMethod(name = "ReturnProcessingWorkflow")
    Contracts.ReturnResult process(Contracts.ReturnRequested input);

    /** The warehouse received the goods. Idempotent. A signal that arrives before the return is authorized is remembered, not lost. */
    @SignalMethod
    void goodsReceived(Contracts.GoodsReceived received);

    /** Authorizing -> RecordingDecision -> AwaitingGoods -> Completing -> Completed | Rejected | AwaitingGoodsExpired. */
    @QueryMethod
    String status();
}
