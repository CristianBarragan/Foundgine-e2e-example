package com.enterprise.erp.returns;

import org.springframework.beans.factory.ObjectProvider;
import org.springframework.http.HttpStatus;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Service;
import org.springframework.web.server.ResponseStatusException;

/**
 * Confirms receipt of returned goods by signalling the return's workflow. The return is looked up by the caller's tenant
 * first, so another tenant's return is simply "not found" and the workflow id can only ever be one of the caller's own.
 * Repeats are harmless: the workflow remembers the signal, and a Completed return answers without signalling.
 */
@Service
public class GoodsReceivedService {
    public enum Outcome { SIGNALLED, ALREADY_COMPLETED }

    private final JdbcClient jdbc;
    private final ObjectProvider<ReturnSignals> signals;

    public GoodsReceivedService(JdbcClient jdbc, ObjectProvider<ReturnSignals> signals) {
        this.jdbc = jdbc;
        this.signals = signals;
    }

    public Outcome receive(String tenantId, int crmReturnId, String receivedBy) {
        var status = jdbc.sql("SELECT status FROM erp_returns WHERE tenant_id = :t AND crm_return_id = :r")
                .param("t", tenantId).param("r", crmReturnId).query(String.class).optional();
        switch (GoodsReceivedPlanner.decide(status.orElse(null))) {
            case NOT_FOUND -> throw new ResponseStatusException(HttpStatus.NOT_FOUND, "Return not found.");
            case NOT_RECEIVABLE -> throw new ResponseStatusException(HttpStatus.CONFLICT, "Return is " + status.get() + "; goods cannot be received.");
            case ALREADY_COMPLETED -> { return Outcome.ALREADY_COMPLETED; }
            case SIGNAL -> { /* below */ }
        }
        var port = signals.getIfAvailable();
        if (port == null) throw new ResponseStatusException(HttpStatus.SERVICE_UNAVAILABLE, "Workflow integration is disabled (temporal.enabled=false).");
        try {
            port.goodsReceived(tenantId, crmReturnId, receivedBy);
        } catch (ReturnWorkflowNotRunningException e) {
            // Typically the 30-day wait expired: the return stays Authorized; an admin resolves it through received-late.
            throw new ResponseStatusException(HttpStatus.CONFLICT, "The return's workflow is no longer waiting for goods; an administrator must resolve it with POST /api/v1/returns/{returnId}/received-late.");
        }
        return Outcome.SIGNALLED;
    }
}
