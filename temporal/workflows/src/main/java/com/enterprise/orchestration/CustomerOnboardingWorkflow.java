package com.enterprise.orchestration;

import io.temporal.workflow.QueryMethod;
import io.temporal.workflow.WorkflowInterface;
import io.temporal.workflow.WorkflowMethod;

/** Name "CustomerOnboardingWorkflow" is what the CRM outbox publisher starts. Id: CustomerOnboarding:{tenant}:{crmId}[:{attempt}]. */
@WorkflowInterface
public interface CustomerOnboardingWorkflow {
    @WorkflowMethod(name = "CustomerOnboardingWorkflow")
    Contracts.OnboardingResult onboard(Contracts.CustomerSync input);

    /** Workflow visibility: CreatingErpCustomer -> RecordingInCrm -> Completed | Failed. */
    @QueryMethod
    String status();
}
