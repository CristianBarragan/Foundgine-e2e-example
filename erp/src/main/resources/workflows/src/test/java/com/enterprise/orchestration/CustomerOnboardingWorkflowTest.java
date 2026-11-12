package com.enterprise.orchestration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.temporal.activity.ActivityInterface;
import io.temporal.activity.ActivityMethod;
import io.temporal.client.WorkflowOptions;
import io.temporal.failure.ApplicationFailure;
import io.temporal.testing.TestWorkflowEnvironment;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class CustomerOnboardingWorkflowTest {
    /** Stand-in for the CRM worker: untyped activities registered by name on the "crm" queue. */
    @ActivityInterface
    public interface CrmStub {
        @ActivityMethod(name = Contracts.CRM_RECORD_ERP_CUSTOMER) void record(Contracts.RecordErpCustomerInput i);
        @ActivityMethod(name = Contracts.CRM_MARK_SYNC_FAILED) void failed(Contracts.MarkSyncFailedInput i);
    }

    static class Crm implements CrmStub {
        final List<Contracts.RecordErpCustomerInput> recorded = new ArrayList<>();
        final List<Contracts.MarkSyncFailedInput> failures = new ArrayList<>();
        public void record(Contracts.RecordErpCustomerInput i) { recorded.add(i); }
        public void failed(Contracts.MarkSyncFailedInput i) { failures.add(i); }
    }

    TestWorkflowEnvironment env;
    Crm crm = new Crm();

    static final Contracts.CustomerSync INPUT =
            new Contracts.CustomerSync(1, "acme", 42, 1, "corr", "Business", "Acme", "a@acme.test", "", "Active");

    @BeforeEach void setUp() { env = TestWorkflowEnvironment.newInstance(); }
    @AfterEach void tearDown() { env.close(); }

    private Contracts.OnboardingResult run(ErpCustomerActivities erp) {
        var orchestration = env.newWorker(Contracts.ORCHESTRATION_QUEUE);
        orchestration.registerWorkflowImplementationTypes(CustomerOnboardingWorkflowImpl.class);
        env.newWorker(Contracts.ERP_QUEUE).registerActivitiesImplementations(erp);
        env.newWorker(Contracts.CRM_QUEUE).registerActivitiesImplementations(crm);
        env.start();
        var wf = env.getWorkflowClient().newWorkflowStub(CustomerOnboardingWorkflow.class,
                WorkflowOptions.newBuilder().setTaskQueue(Contracts.ORCHESTRATION_QUEUE).setWorkflowId("CustomerOnboarding:acme:42").build());
        return wf.onboard(INPUT);
    }

    @Test void happyPathRecordsTheErpIdInTheCrm() {
        var result = run(in -> new Contracts.ErpCustomerResult("ERP-9001", true));
        assertEquals("Completed", result.status());
        assertEquals("ERP-9001", crm.recorded.get(0).erpCustomerId());
        assertTrue(crm.failures.isEmpty());
    }

    @Test void validationFailureIsNotRetriedAndMarksTheCrmMappingFailed() {
        var calls = new int[1];
        var result = run(in -> { calls[0]++; throw ApplicationFailure.newNonRetryableFailure("name is required", "ValidationFailed"); });
        assertEquals("Failed", result.status());
        assertEquals(1, calls[0]);
        assertEquals("name is required", crm.failures.get(0).reason());
        assertTrue(crm.recorded.isEmpty());
    }

    @Test void transientErpFailureIsRetriedThenSucceeds() {
        var calls = new int[1];
        var result = run(in -> {
            if (++calls[0] < 3) throw ApplicationFailure.newFailure("erp db down", "Transient");
            return new Contracts.ErpCustomerResult("ERP-9002", true);
        });
        assertEquals("Completed", result.status());
        assertEquals(3, calls[0]);
    }
}
