package com.enterprise.erp.fulfilment;

import com.enterprise.erp.security.ErpCaller;
import java.util.Map;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/** Authenticated ERP lifecycle commands. Tenant identity is always derived from the validated JWT. */
@RestController
@RequestMapping("/api/v1")
class FulfilmentController {
    private final ErpFulfilmentService service;
    FulfilmentController(ErpFulfilmentService service) { this.service = service; }

    @PostMapping("/orders/{orderId}/shipments")
    @PreAuthorize("hasAnyRole('erp-inventory','erp-admin','enterprise-admin')")
    ResponseEntity<ErpFulfilmentService.ShipmentView> createShipment(@PathVariable String orderId,
            @RequestBody ErpFulfilmentService.CreateShipment body, Authentication auth) {
        return ResponseEntity.status(HttpStatus.CREATED).body(service.createShipment(ErpCaller.from(auth).requireTenant(),orderId,body));
    }

    @PostMapping("/shipments/{shipmentId}/transitions")
    @PreAuthorize("hasAnyRole('erp-inventory','erp-admin','enterprise-admin')")
    ErpFulfilmentService.ShipmentView transitionShipment(@PathVariable String shipmentId,
            @RequestBody Map<String,String> body, Authentication auth) {
        return service.transitionShipment(ErpCaller.from(auth).requireTenant(),shipmentId,body.get("status"));
    }

    @PostMapping("/orders/{orderId}/invoices")
    @PreAuthorize("hasAnyRole('erp-finance','erp-admin','enterprise-admin')")
    ResponseEntity<ErpFulfilmentService.InvoiceView> issueInvoice(@PathVariable String orderId,
            @RequestBody(required=false) ErpFulfilmentService.IssueInvoice body, Authentication auth) {
        return ResponseEntity.status(HttpStatus.CREATED).body(service.issueInvoice(ErpCaller.from(auth).requireTenant(),orderId,body));
    }

    @PostMapping("/invoices/{invoiceId}/payments")
    @PreAuthorize("hasAnyRole('erp-finance','erp-admin','enterprise-admin')")
    ResponseEntity<ErpFulfilmentService.PaymentView> receivePayment(@PathVariable String invoiceId,
            @RequestBody ErpFulfilmentService.ReceivePayment body, Authentication auth) {
        return ResponseEntity.status(HttpStatus.CREATED).body(service.receivePayment(ErpCaller.from(auth).requireTenant(),invoiceId,body));
    }

    @PostMapping("/payments/{paymentId}/reverse")
    @PreAuthorize("hasAnyRole('erp-finance','erp-admin','enterprise-admin')")
    ErpFulfilmentService.PaymentView reversePayment(@PathVariable String paymentId, Authentication auth) {
        return service.reversePayment(ErpCaller.from(auth).requireTenant(),paymentId);
    }

    @PostMapping("/payments/{paymentId}/reconcile")
    @PreAuthorize("hasAnyRole('erp-finance','erp-admin','enterprise-admin')")
    ErpFulfilmentService.PaymentView reconcilePayment(@PathVariable String paymentId, Authentication auth) {
        return service.reconcilePayment(ErpCaller.from(auth).requireTenant(),paymentId);
    }
}
