package com.enterprise.erp.returns;

import static com.enterprise.erp.returns.GoodsReceivedPlanner.Plan.ALREADY_COMPLETED;
import static com.enterprise.erp.returns.GoodsReceivedPlanner.Plan.NOT_FOUND;
import static com.enterprise.erp.returns.GoodsReceivedPlanner.Plan.NOT_RECEIVABLE;
import static com.enterprise.erp.returns.GoodsReceivedPlanner.Plan.SIGNAL;
import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

class GoodsReceivedPlannerTest {
    @Test void authorizedReturnIsSignalled() { assertEquals(SIGNAL, GoodsReceivedPlanner.decide("Authorized")); }
    @Test void completedReturnIsARepeat() { assertEquals(ALREADY_COMPLETED, GoodsReceivedPlanner.decide("Completed")); }
    @Test void unknownReturnIsNotFound() { assertEquals(NOT_FOUND, GoodsReceivedPlanner.decide(null)); }
    @Test void rejectedOrUnexpectedStatusCannotReceiveGoods() {
        assertEquals(NOT_RECEIVABLE, GoodsReceivedPlanner.decide("Rejected"));
        assertEquals(NOT_RECEIVABLE, GoodsReceivedPlanner.decide("Requested"));
    }

    @Test void workflowIdMatchesTheCrmAndTheWorkflowConvention() {
        assertEquals("ReturnProcessing:acme:7", ReturnSignals.workflowId("acme", 7));
    }
}
