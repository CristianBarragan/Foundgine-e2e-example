package com.enterprise.erp.customer;

import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface ErpCustomerRepository extends JpaRepository<ErpCustomer, String> {
    Optional<ErpCustomer> findByTenantIdAndCrmCustomerId(String tenantId, int crmCustomerId);

    boolean existsByErpCustomerIdAndTenantIdAndCrmCustomerIdAndStatus(String erpCustomerId, String tenantId, int crmCustomerId, String status);

    /**
     * Insert-if-absent. A plain save() would raise a unique violation when two identical requests race, and that aborts
     * the PostgreSQL transaction; ON CONFLICT DO NOTHING lets the loser carry on and read the winner's row.
     */
    @Modifying(flushAutomatically = true)
    @Query(value = """
            INSERT INTO erp_customers (erp_customer_id, tenant_id, crm_customer_id, kind, name, email, phone, status)
            VALUES ('ERP-' || nextval('erp_customer_seq'), :tenant, :crm, :kind, :name, :email, :phone, :status)
            ON CONFLICT (tenant_id, crm_customer_id) DO NOTHING""", nativeQuery = true)
    int insertIfAbsent(@Param("tenant") String tenant, @Param("crm") int crm, @Param("kind") String kind,
                       @Param("name") String name, @Param("email") String email, @Param("phone") String phone,
                       @Param("status") String status);

    @Modifying(flushAutomatically = true)
    @Query(value = """
            INSERT INTO customer_inbox (tenant_id, correlation_id, crm_customer_id, erp_customer_id, attempt)
            VALUES (:tenant, :corr, :crm, :erp, :attempt) ON CONFLICT (tenant_id, correlation_id) DO NOTHING""", nativeQuery = true)
    int recordInbox(@Param("tenant") String tenant, @Param("corr") String corr, @Param("crm") int crm,
                    @Param("erp") String erp, @Param("attempt") int attempt);
}
