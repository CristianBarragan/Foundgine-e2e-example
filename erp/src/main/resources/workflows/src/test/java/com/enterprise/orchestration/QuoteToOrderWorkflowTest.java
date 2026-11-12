package com.enterprise.orchestration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.temporal.activity.ActivityInterface;
import io.temporal.activity.ActivityMethod;
import io.temporal.client.WorkflowOptions;
import io.temporal.failure.ApplicationFailure;
import io.temporal.testing.TestWorkflowEnvironment;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class QuoteToOrderWorkflowTest {
    /** Stand-in for the CRM worker (untyped activities registered by name on the "crm" queue). */
    @ActivityInterface
    public interface CrmStub {
        @ActivityMethod(name = Contracts.CRM_RECORD_ERP_ORDER) void record(Contracts.RecordErpOrderInput i);
        @ActivityMethod(name = Contracts.CRM_MARK_ORDER_FAILED) void markFailed(Contracts.MarkOrderFailedInput i);
    }

    /** Records both CRM calls. */
    class Crm implements CrmStub {
        RuntimeException recordFailure;
        public void record(Contracts.RecordErpOrderInput i) { if (recordFailure != null) throw recordFailure; recorded.add(i); }
        public void markFailed(Contracts.MarkOrderFailedInput i) { failures.add(i); }
    }

    static class Erp implements ErpOrderActivities {
        final List<Contracts.CancelOrderRequest> cancelled = new ArrayList<>();
        int createCalls;
        int transientFailures;                 // fail this many calls with a retryable error first
        RuntimeException createFailure;
        public Contracts.ErpOrderResult createOrderWithReservation(Contracts.QuoteAccepted in) {
            createCalls++;
            if (createCalls <= transientFailures) throw ApplicationFailure.newFailure("erp db down", "Transient");
            if (createFailure != null) throw createFailure;
            return new Contracts.ErpOrderResult("SO-5001", true);
        }
        public Contracts.CancelOrderResult cancelOrder(Contracts.CancelOrderRequest in) {
            cancelled.add(in);
            return new Contracts.CancelOrderResult(in.erpOrderId(), true);
        }
    }

    static final Contracts.QuoteAccepted INPUT = new Contracts.QuoteAccepted(1, "acme", 7, 3, 42, "ERP-9001", "corr",
            new BigDecimal("200.00"), List.of(new Contracts.QuoteLine("SKU-1", "Widget", 2, new BigDecimal("100.00"))));

    TestWorkflowEnvironment env;
    Erp erp = new Erp();
    final List<Contracts.RecordErpOrderInput> recorded = new ArrayList<>();
    final List<Contracts.MarkOrderFailedInput> failures = new ArrayList<>();

    @BeforeEach void setUp() { env = TestWorkflowEnvironment.newInstance(); }
    @AfterEach void tearDown() { env.close(); }

    private Contracts.QuoteToOrderResult run(CrmStub crm) {
        env.newWorker(Contracts.ORCHESTRATION_QUEUE).registerWorkflowImplementationTypes(QuoteToOrderWorkflowImpl.class);
        env.newWorker(Contracts.ERP_QUEUE).registerActivitiesImplementations(erp);
        env.newWorker(Contracts.CRM_QUEUE).registerActivitiesImplementations(crm);
        env.start();
        var wf = env.getWorkflowClient().newWorkflowStub(QuoteToOrderWorkflow.class,
                WorkflowOptions.newBuilder().setTaskQueue(Contracts.ORCHESTRATION_QUEUE).setWorkflowId("QuoteToOrder:acme:7").build());
        return wf.place(INPUT);
    }

    @Test void happyPathCreatesTheOrderAndRecordsItInTheCrm() {
        var result = run(new Crm());
        assertEquals("Completed", result.status());
        assertEquals("SO-5001", result.erpOrderId());
        assertEquals(new Contracts.RecordErpOrderInput("acme", 7, "SO-5001"), recorded.get(0));
        assertTrue(erp.cancelled.isEmpty());
    }

    @Test void insufficientStockIsNotRetriedAndTheFailureIsReportedToTheCrm() {
        erp.createFailure = ApplicationFailure.newNonRetryableFailure("Insufficient stock for SKU-1 (requested 2).", "InsufficientStock");
        var result = run(new Crm());
        assertEquals("Failed", result.status());
        assertEquals(1, erp.createCalls);
        assertTrue(recorded.isEmpty());
        assertTrue(erp.cancelled.isEmpty());   // nothing was created, so nothing to compensate
        assertEquals(1, failures.size());      // but the CRM quote is told, with the reason
        assertEquals(7, failures.get(0).quoteId());
        assertTrue(failures.get(0).reason().contains("Insufficient stock"));
    }

    @Test void transientErpFailureIsRetried() {
        erp.transientFailures = 2;
        var result = run(new Crm());
        assertEquals("Completed", result.status());
        assertEquals(3, erp.createCalls);
    }

    @Test void permanentCrmRefusalCancelsTheErpOrder() {
        var crm = new Crm();
        crm.recordFailure = ApplicationFailure.newNonRetryableFailure("The quote already has a different ERP order.", "CrmConflictException");
        var result = run(crm);
        assertEquals("Failed", result.status());
        assertEquals(1, failures.size());
        assertEquals(1, erp.cancelled.size());
        assertEquals("SO-5001", erp.cancelled.get(0).erpOrderId());
        assertEquals(7, erp.cancelled.get(0).quoteId());
    }
}
