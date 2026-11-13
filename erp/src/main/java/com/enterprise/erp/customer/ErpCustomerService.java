package com.enterprise.erp.customer;

import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * ERP side of customer onboarding. Idempotent: the natural key (tenant, CRM customer id) is unique, so the same
 * message delivered twice (or the same customer re-onboarded by a later attempt) returns the existing ERP id.
 * The ERP id is minted here and is never derived from the CRM id.
 */
@Service
public class ErpCustomerService {
    private final ErpCustomerRepository customers;

    public ErpCustomerService(ErpCustomerRepository customers) { this.customers = customers; }

    @Transactional
    public ErpCustomerResult createOrGet(CustomerSync in) {
        var errors = CustomerValidator.validate(in);
        if (!errors.isEmpty()) throw new ValidationFailedException(errors);

        int inserted = customers.insertIfAbsent(in.tenantId(), in.crmCustomerId(), in.kind(), in.name().trim(),
                in.email() == null ? "" : in.email().trim(), in.phone() == null ? "" : in.phone().trim(), in.status());

        String erpId = customers.findByTenantIdAndCrmCustomerId(in.tenantId(), in.crmCustomerId())
                .orElseThrow().getErpCustomerId();

        customers.recordInbox(in.tenantId(), in.correlationId(), in.crmCustomerId(), erpId, in.attempt());

        return new ErpCustomerResult(erpId, inserted == 1);
    }
}
