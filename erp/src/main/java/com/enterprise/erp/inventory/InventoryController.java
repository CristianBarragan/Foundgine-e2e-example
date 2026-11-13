package com.enterprise.erp.inventory;

import com.enterprise.erp.security.ErpCaller;
import java.util.List;
import org.springframework.http.ResponseEntity;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/** Stock levels for the caller's tenant; the tenant always comes from the validated JWT, never from the request. */
@RestController
@RequestMapping("/api/v1/inventory")
class InventoryController {
    private final ErpInventoryService service;
    InventoryController(ErpInventoryService service) { this.service = service; }

    @GetMapping
    @PreAuthorize("hasAnyRole('erp-inventory','erp-sales','erp-admin','enterprise-admin')")
    List<ErpInventoryService.StockView> list(Authentication auth) {
        return service.list(ErpCaller.from(auth).requireTenant());
    }

    @GetMapping("/{sku}")
    @PreAuthorize("hasAnyRole('erp-inventory','erp-sales','erp-admin','enterprise-admin')")
    ResponseEntity<ErpInventoryService.StockView> get(@PathVariable String sku, Authentication auth) {
        return service.find(ErpCaller.from(auth).requireTenant(), sku)
                .map(ResponseEntity::ok).orElseGet(() -> ResponseEntity.notFound().build());
    }
}
