package com.enterprise.erp.returns;

import com.enterprise.erp.security.ErpCaller;
import java.util.Map;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.server.ResponseStatusException;

/**
 * The only authorized sender of the goodsReceived signal, plus the admin-only operator path for an expired wait. Warehouse (erp-inventory) and admins confirm receipt of a return;
 * the tenant and the sender come from the validated token, never from the request, so the signal always carries a real
 * identity and can only reach a workflow of the caller's own tenant. {returnId} is the CRM return id (as in the workflow id).
 */
@RestController
@RequestMapping("/api/v1/returns")
class ReturnController {
    private final GoodsReceivedService service;
    private final ExpiredReturnService expired;

    ReturnController(GoodsReceivedService service, ExpiredReturnService expired) {
        this.service = service;
        this.expired = expired;
    }

    @PostMapping("/{returnId}/goods-received")
    @PreAuthorize("hasAnyRole('erp-inventory', 'erp-admin', 'enterprise-admin')")
    ResponseEntity<Map<String, String>> goodsReceived(@PathVariable int returnId, Authentication authentication) {
        if (returnId <= 0) throw new ResponseStatusException(HttpStatus.BAD_REQUEST, "returnId must be positive.");
        var caller = ErpCaller.from(authentication);
        var tenant = caller.requireTenant();
        var outcome = service.receive(tenant, returnId, caller.subject());
        return outcome == GoodsReceivedService.Outcome.SIGNALLED
                ? ResponseEntity.status(HttpStatus.ACCEPTED).body(Map.of("status", "signalled"))
                : ResponseEntity.ok(Map.of("status", "alreadyCompleted"));
    }

    /**
     * Operator path: the goods arrived after the 30-day wait expired, so the original workflow no longer listens. Admins only
     * (warehouse staff confirm receipt through goods-received while the workflow still waits). Starts a ReturnResolutionWorkflow
     * that puts the stock back and completes the return in the CRM; the response only says it was started.
     */
    @PostMapping("/{returnId}/received-late")
    @PreAuthorize("hasAnyRole('erp-admin', 'enterprise-admin')")
    ResponseEntity<Map<String, String>> receivedLate(@PathVariable int returnId, Authentication authentication) {
        if (returnId <= 0) throw new ResponseStatusException(HttpStatus.BAD_REQUEST, "returnId must be positive.");
        var caller = ErpCaller.from(authentication);
        var tenant = caller.requireTenant();
        return switch (expired.resolveReceivedLate(tenant, returnId, caller.subject())) {
            case STARTED -> ResponseEntity.status(HttpStatus.ACCEPTED).body(Map.of("status", "resolving"));
            case ALREADY_IN_PROGRESS -> ResponseEntity.ok(Map.of("status", "alreadyInProgress"));
            case ALREADY_COMPLETED -> ResponseEntity.ok(Map.of("status", "alreadyCompleted"));
        };
    }
}
