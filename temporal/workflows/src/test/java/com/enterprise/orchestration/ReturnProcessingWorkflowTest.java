package com.enterprise.orchestration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.temporal.activity.ActivityInterface;
import io.temporal.activity.ActivityMethod;
import io.temporal.client.WorkflowClient;
import io.temporal.client.WorkflowOptions;
import io.temporal.failure.ApplicationFailure;
import io.temporal.testing.TestWorkflowEnvironment;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class ReturnProcessingWorkflowTest {
    @ActivityInterface
    public interface CrmStub {
        @ActivityMethod(name = Contracts.CRM_RECORD_RETURN_DECISION) void decide(Contracts.RecordReturnDecisionInput i);
    }

    static class Crm implements CrmStub {
        final List<Contracts.RecordReturnDecisionInput> decisions = new ArrayList<>();
        public void decide(Contracts.RecordReturnDecisionInput i) { decisions.add(i); }
    }

    static class Erp implements ErpReturnActivities {
        String decision = "Authorized";
        String note = "";
        RuntimeException authorizeFailure;
        final List<Contracts.CompleteReturnRequest> completed = new ArrayList<>();
        public Contracts.ErpReturnDecision authorizeReturn(Contracts.ReturnRequested in) {
            if (authorizeFailure != null) throw authorizeFailure;
            return new Contracts.ErpReturnDecision(decision, "RET-7001", note, true);
        }
        public Contracts.CompleteReturnResult completeReturn(Contracts.CompleteReturnRequest in) {
            completed.add(in);
            return new Contracts.CompleteReturnResult(in.erpReturnId(), true);
        }
    }

    static final Contracts.ReturnRequested INPUT = new Contracts.ReturnRequested(1, "acme", 5, 7, 42, "ERP-9001", "SO-5001",
            "corr", "damaged", List.of(new Contracts.ReturnLine("A", 2)));

    TestWorkflowEnvironment env;
    Erp erp = new Erp();
    Crm crm = new Crm();

    @BeforeEach void setUp() { env = TestWorkflowEnvironment.newInstance(); }
    @AfterEach void tearDown() { env.close(); }

    private ReturnProcessingWorkflow start() {
        env.newWorker(Contracts.ORCHESTRATION_QUEUE).registerWorkflowImplementationTypes(ReturnProcessingWorkflowImpl.class);
        env.newWorker(Contracts.ERP_QUEUE).registerActivitiesImplementations(erp);
        env.newWorker(Contracts.CRM_QUEUE).registerActivitiesImplementations(crm);
        env.start();
        return env.getWorkflowClient().newWorkflowStub(ReturnProcessingWorkflow.class,
                WorkflowOptions.newBuilder().setTaskQueue(Contracts.ORCHESTRATION_QUEUE).setWorkflowId("ReturnProcessing:acme:5").build());
    }

    @Test void authorizedReturnWaitsForTheGoodsThenCompletes() {
        var wf = start();
        var future = WorkflowClient.execute(wf::process, INPUT);
        env.sleep(Duration.ofMinutes(5));
        assertEquals("AwaitingGoods", wf.status());
        assertEquals("Authorized", crm.decisions.get(0).decision());
        assertEquals("RET-7001", crm.decisions.get(0).erpReturnId());
        assertTrue(erp.completed.isEmpty());

        wf.goodsReceived(new Contracts.GoodsReceived("warehouse-1"));
        var result = future.join();
        assertEquals("Completed", result.status());
        assertEquals(1, erp.completed.size());
        assertEquals("Completed", crm.decisions.get(1).decision());
    }

    @Test void rejectedReturnIsReportedAndNeverCompleted() {
        erp.decision = "Rejected";
        erp.note = "Cannot return 3 of A: only 2 of 5 remain returnable.";
        var result = start().process(INPUT);
        assertEquals("Rejected", result.status());
        assertEquals(1, crm.decisions.size());
        assertEquals("Rejected", crm.decisions.get(0).decision());
        assertTrue(crm.decisions.get(0).note().contains("only 2 of 5"));
        assertTrue(erp.completed.isEmpty());
    }

    @Test void malformedRequestBecomesARejection() {
        erp.authorizeFailure = ApplicationFailure.newNonRetryableFailure("At least one line is required.", "ValidationFailed");
        var result = start().process(INPUT);
        assertEquals("Rejected", result.status());
        assertEquals("At least one line is required.", crm.decisions.get(0).note());
    }

    @Test void goodsNeverArrivingLeavesTheReturnAuthorized() {
        var wf = start();
        var future = WorkflowClient.execute(wf::process, INPUT);
        env.sleep(Duration.ofDays(31));
        assertEquals("AwaitingGoodsExpired", future.join().status());
        assertEquals(1, crm.decisions.size());   // only the Authorized decision was recorded
        assertTrue(erp.completed.isEmpty());
    }
}
