package com.enterprise.erp.customer;

import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * ERP side of customer onboarding. Idempotent: the natural key (tenant, CRM customer id) is unique, so the same
 * message delivered twice (or the same customer re-onboarded by a later attempt) returns the existing ERP id.
 * The ERP id is minted here and is never derived from the CRM id.
 */
@Service
public class ErpCustomerService {
    private final JdbcClient jdbc;

    public ErpCustomerService(JdbcClient jdbc) { this.jdbc = jdbc; }

    @Transactional
    public ErpCustomerResult createOrGet(CustomerSync in) {
        var errors = CustomerValidator.validate(in);
        if (!errors.isEmpty()) throw new ValidationFailedException(errors);

        int inserted = jdbc.sql("""
                INSERT INTO erp_customers (erp_customer_id, tenant_id, crm_customer_id, kind, name, email, phone, status)
                VALUES ('ERP-' || nextval('erp_customer_seq'), :tenant, :crm, :kind, :name, :email, :phone, :status)
                ON CONFLICT (tenant_id, crm_customer_id) DO NOTHING""")
                .param("tenant", in.tenantId()).param("crm", in.crmCustomerId()).param("kind", in.kind())
                .param("name", in.name().trim()).param("email", in.email() == null ? "" : in.email().trim())
                .param("phone", in.phone() == null ? "" : in.phone().trim()).param("status", in.status())
                .update();

        String erpId = jdbc.sql("SELECT erp_customer_id FROM erp_customers WHERE tenant_id = :tenant AND crm_customer_id = :crm")
                .param("tenant", in.tenantId()).param("crm", in.crmCustomerId())
                .query(String.class).single();

        jdbc.sql("""
                INSERT INTO customer_inbox (tenant_id, correlation_id, crm_customer_id, erp_customer_id, attempt)
                VALUES (:tenant, :corr, :crm, :erp, :attempt) ON CONFLICT (tenant_id, correlation_id) DO NOTHING""")
                .param("tenant", in.tenantId()).param("corr", in.correlationId()).param("crm", in.crmCustomerId())
                .param("erp", erpId).param("attempt", in.attempt()).update();

        return new ErpCustomerResult(erpId, inserted == 1);
    }
}
