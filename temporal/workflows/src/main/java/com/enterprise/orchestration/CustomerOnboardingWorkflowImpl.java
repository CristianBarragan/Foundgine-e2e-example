package com.enterprise.orchestration;

import io.temporal.activity.ActivityOptions;
import io.temporal.common.RetryOptions;
import io.temporal.failure.ActivityFailure;
import io.temporal.failure.ApplicationFailure;
import io.temporal.workflow.ActivityStub;
import io.temporal.workflow.Workflow;
import java.time.Duration;

/**
 * 1. ERP validates and creates its customer (idempotent on tenant + CRM id).
 * 2. CRM records the ERP id in its mapping (idempotent; a different ERP id is a conflict).
 * If either fails for good, the CRM mapping is marked Failed so reconciliation can re-queue a new attempt; the ERP step
 * is safe to repeat because it returns the existing customer. No tokens or secrets are ever passed through here.
 */
public class CustomerOnboardingWorkflowImpl implements CustomerOnboardingWorkflow {
    private static final String VALIDATION_FAILED = "ValidationFailed";

    private static RetryOptions retry() {
        return RetryOptions.newBuilder()
                .setInitialInterval(Duration.ofSeconds(1)).setBackoffCoefficient(2.0)
                .setMaximumInterval(Duration.ofMinutes(1)).setMaximumAttempts(10)
                .setDoNotRetry(VALIDATION_FAILED, "CrmNotFoundException", "CrmValidationException")
                .build();
    }

    private static ActivityOptions options(String queue) {
        return ActivityOptions.newBuilder().setTaskQueue(queue).setStartToCloseTimeout(Duration.ofSeconds(30))
                .setRetryOptions(retry()).build();
    }

    private final ErpCustomerActivities erp = Workflow.newActivityStub(ErpCustomerActivities.class, options(Contracts.ERP_QUEUE));
    private final ActivityStub crm = Workflow.newUntypedActivityStub(options(Contracts.CRM_QUEUE));
    private String status = "Started";

    @Override
    public Contracts.OnboardingResult onboard(Contracts.CustomerSync in) {
        try {
            status = "CreatingErpCustomer";
            var created = erp.validateAndCreateCustomer(in);
            status = "RecordingInCrm";
            crm.execute(Contracts.CRM_RECORD_ERP_CUSTOMER, Void.class,
                    new Contracts.RecordErpCustomerInput(in.tenantId(), in.crmCustomerId(), created.erpCustomerId()));
            status = "Completed";
            return new Contracts.OnboardingResult("Completed", created.erpCustomerId(), "");
        } catch (ActivityFailure e) {
            var reason = reasonOf(e);
            status = "Failed";
            crm.execute(Contracts.CRM_MARK_SYNC_FAILED, Void.class,
                    new Contracts.MarkSyncFailedInput(in.tenantId(), in.crmCustomerId(), reason));
            return new Contracts.OnboardingResult("Failed", "", reason);
        }
    }

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
