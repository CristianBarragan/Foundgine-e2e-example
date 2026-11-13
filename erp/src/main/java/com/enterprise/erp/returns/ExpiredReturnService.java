package com.enterprise.erp.returns;

import org.springframework.beans.factory.ObjectProvider;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;
import org.springframework.web.server.ResponseStatusException;

/**
 * Operator path for an Authorized return whose workflow stopped waiting (the 30-day wait expired). The return is looked up by
 * the caller's tenant first, so another tenant's return is "not found" and the ids sent to Temporal are always the caller's own.
 * The ERP return id comes from the ERP's own row, never from the request. Repeats are harmless: a second start is refused by
 * Temporal (same workflow id) and a Completed return answers without starting anything.
 */
@Service
public class ExpiredReturnService {
    public enum Outcome { STARTED, ALREADY_IN_PROGRESS, ALREADY_COMPLETED }

    private final ErpReturnRepository returns;
    private final ObjectProvider<ReturnResolutions> resolutions;

    public ExpiredReturnService(ErpReturnRepository returns, ObjectProvider<ReturnResolutions> resolutions) {
        this.returns = returns;
        this.resolutions = resolutions;
    }

    public Outcome resolveReceivedLate(String tenantId, int crmReturnId, String resolvedBy) {
        // Deliberately not @Transactional: starting the workflow below is a remote call and must not hold a database connection.
        var row = returns.findByTenantIdAndCrmReturnId(tenantId, crmReturnId);
        var port = resolutions.getIfAvailable();
        var plan = ExpiredReturnPlanner.decide(row.map(ErpReturn::getStatus).orElse(null), () -> {
            if (port == null) throw new ResponseStatusException(HttpStatus.SERVICE_UNAVAILABLE, "Workflow integration is disabled (temporal.enabled=false).");
            return port.originalWorkflow(tenantId, crmReturnId);
        });
        switch (plan) {
            case NOT_FOUND -> throw new ResponseStatusException(HttpStatus.NOT_FOUND, "Return not found.");
            case NOT_RESOLVABLE -> throw new ResponseStatusException(HttpStatus.CONFLICT, "Return is " + row.get().getStatus() + "; there is nothing to resolve.");
            case STILL_WAITING -> throw new ResponseStatusException(HttpStatus.CONFLICT, "The return is still waiting for goods; confirm receipt with goods-received instead.");
            case ALREADY_COMPLETED -> { return Outcome.ALREADY_COMPLETED; }
            case RESOLVE -> { /* below */ }
        }
        return port.resolveReceivedLate(tenantId, crmReturnId, row.get().getErpReturnId(), resolvedBy) ? Outcome.STARTED : Outcome.ALREADY_IN_PROGRESS;
    }
}
