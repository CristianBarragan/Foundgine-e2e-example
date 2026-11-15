package com.enterprise.orchestration;

import io.temporal.workflow.QueryMethod;
import io.temporal.workflow.WorkflowInterface;
import io.temporal.workflow.WorkflowMethod;

/** Name "QuoteToOrderWorkflow" is what the CRM outbox publisher starts. Id: QuoteToOrder:{tenant}:{quoteId}. */
@WorkflowInterface
public interface QuoteToOrderWorkflow {
    @WorkflowMethod(name = "QuoteToOrderWorkflow")
    Contracts.QuoteToOrderResult place(Contracts.QuoteAccepted input);

    /** Workflow visibility: CreatingErpOrder -> RecordingInCrm -> Completed | Compensating -> Failed. */
    @QueryMethod
    String status();
}
