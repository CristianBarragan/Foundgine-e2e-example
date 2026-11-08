package com.enterprise.erp.returns;

import static com.enterprise.erp.returns.ExpiredReturnPlanner.Plan.ALREADY_COMPLETED;
import static com.enterprise.erp.returns.ExpiredReturnPlanner.Plan.NOT_FOUND;
import static com.enterprise.erp.returns.ExpiredReturnPlanner.Plan.NOT_RESOLVABLE;
import static com.enterprise.erp.returns.ExpiredReturnPlanner.Plan.RESOLVE;
import static com.enterprise.erp.returns.ExpiredReturnPlanner.Plan.STILL_WAITING;
import static com.enterprise.erp.returns.ReturnResolutions.WorkflowState.CLOSED;
import static com.enterprise.erp.returns.ReturnResolutions.WorkflowState.RUNNING;
import static com.enterprise.erp.returns.ReturnResolutions.WorkflowState.UNKNOWN;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.fail;

import org.junit.jupiter.api.Test;

class ExpiredReturnPlannerTest {
    @Test void authorizedReturnWithAClosedWorkflowIsResolved() { assertEquals(RESOLVE, ExpiredReturnPlanner.decide("Authorized", () -> CLOSED)); }
    @Test void authorizedReturnWhoseWorkflowHistoryIsGoneIsResolved() { assertEquals(RESOLVE, ExpiredReturnPlanner.decide("Authorized", () -> UNKNOWN)); }
    @Test void authorizedReturnStillWaitingUsesGoodsReceivedInstead() { assertEquals(STILL_WAITING, ExpiredReturnPlanner.decide("Authorized", () -> RUNNING)); }
    @Test void completedReturnIsARepeat() { assertEquals(ALREADY_COMPLETED, ExpiredReturnPlanner.decide("Completed", () -> fail("Temporal must not be asked"))); }
    @Test void unknownReturnIsNotFound() { assertEquals(NOT_FOUND, ExpiredReturnPlanner.decide(null, () -> fail("Temporal must not be asked"))); }
    @Test void rejectedOrUnexpectedStatusHasNothingToResolve() {
        assertEquals(NOT_RESOLVABLE, ExpiredReturnPlanner.decide("Rejected", () -> fail("Temporal must not be asked")));
        assertEquals(NOT_RESOLVABLE, ExpiredReturnPlanner.decide("Requested", () -> fail("Temporal must not be asked")));
    }

    @Test void resolutionWorkflowIdIsTenantScopedAndDistinctFromTheOriginal() {
        assertEquals("ReturnResolution:acme:7", ReturnResolutions.workflowId("acme", 7));
        assertEquals("ReturnProcessing:acme:7", ReturnSignals.workflowId("acme", 7));
    }
}
