package com.enterprise.orchestration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.temporal.activity.ActivityInterface;
import io.temporal.activity.ActivityMethod;
import io.temporal.client.WorkflowFailedException;
import io.temporal.client.WorkflowOptions;
import io.temporal.testing.TestWorkflowEnvironment;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class ReturnResolutionWorkflowTest {
    @ActivityInterface
    public interface CrmStub {
        @ActivityMethod(name = Contracts.CRM_RECORD_RETURN_DECISION) void decide(Contracts.RecordReturnDecisionInput i);
    }

    static class Crm implements CrmStub {
        final List<Contracts.RecordReturnDecisionInput> decisions = new ArrayList<>();
        public void decide(Contracts.RecordReturnDecisionInput i) { decisions.add(i); }
    }

    static class Erp implements ErpReturnActivities {
        final List<Contracts.CompleteReturnRequest> completed = new ArrayList<>();
        public Contracts.ErpReturnDecision authorizeReturn(Contracts.ReturnRequested in) { throw new IllegalStateException("not used"); }
        public Contracts.CompleteReturnResult completeReturn(Contracts.CompleteReturnRequest in) {
            completed.add(in);
            return new Contracts.CompleteReturnResult(in.erpReturnId(), true);
        }
    }

    TestWorkflowEnvironment env;
    Erp erp = new Erp();
    Crm crm = new Crm();

    @BeforeEach void setUp() { env = TestWorkflowEnvironment.newInstance(); }
    @AfterEach void tearDown() { env.close(); }

    private ReturnResolutionWorkflow start() {
        env.newWorker(Contracts.ORCHESTRATION_QUEUE).registerWorkflowImplementationTypes(ReturnResolutionWorkflowImpl.class);
        env.newWorker(Contracts.ERP_QUEUE).registerActivitiesImplementations(erp);
        env.newWorker(Contracts.CRM_QUEUE).registerActivitiesImplementations(crm);
        env.start();
        return env.getWorkflowClient().newWorkflowStub(ReturnResolutionWorkflow.class,
                WorkflowOptions.newBuilder().setTaskQueue(Contracts.ORCHESTRATION_QUEUE).setWorkflowId("ReturnResolution:acme:5").build());
    }

    @Test void lateReceiptPutsStockBackAndCompletesTheCrmReturn() {
        var result = start().resolve(new Contracts.ResolveExpiredReturn("acme", 5, "RET-7001", "ops-user"));
        assertEquals("Completed", result.status());
        assertEquals(1, erp.completed.size());
        assertEquals("RET-7001", erp.completed.get(0).erpReturnId());
        assertEquals(1, crm.decisions.size());
        assertEquals("Completed", crm.decisions.get(0).decision());
        assertEquals(5, crm.decisions.get(0).returnId());
        assertTrue(crm.decisions.get(0).note().contains("ops-user"));
    }

    @Test void inputWithoutAnOperatorIsRefusedBeforeAnythingChanges() {
        var wf = start();
        var ex = assertThrows(WorkflowFailedException.class, () -> wf.resolve(new Contracts.ResolveExpiredReturn("acme", 5, "RET-7001", " ")));
        assertTrue(ex.getCause().getMessage().contains("required"));
        assertTrue(erp.completed.isEmpty());
        assertTrue(crm.decisions.isEmpty());
    }
}
