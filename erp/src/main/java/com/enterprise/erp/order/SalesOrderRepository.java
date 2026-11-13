package com.enterprise.erp.order;

import jakarta.persistence.LockModeType;
import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Lock;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface SalesOrderRepository extends JpaRepository<SalesOrder, String> {
    Optional<SalesOrder> findByTenantIdAndCrmQuoteId(String tenantId, int crmQuoteId);

    /** SELECT ... FOR UPDATE: cancellation and shipping both lock the order row first (same lock order everywhere). */
    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("select o from SalesOrder o where o.erpOrderId = :id and o.tenantId = :tenant and o.crmQuoteId = :quote")
    Optional<SalesOrder> lockForCancel(@Param("id") String id, @Param("tenant") String tenant, @Param("quote") int quote);

    /**
     * SELECT ... FOR UPDATE by (tenant, order id): the lock every order-scoped writer takes first (shipping, invoicing,
     * return authorisation), so they all serialise on the order row and always lock in the same order.
     */
    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("select o from SalesOrder o where o.tenantId = :tenant and o.erpOrderId = :id")
    Optional<SalesOrder> lockByTenantAndId(@Param("tenant") String tenant, @Param("id") String id);

    boolean existsByTenantIdAndErpOrderIdAndStatus(String tenantId, String erpOrderId, String status);

    /** The CRM quote an ERP order was created for (the outbox row carries it so the relay can find the workflow). */
    @Query("select o.crmQuoteId from SalesOrder o where o.tenantId = :tenant and o.erpOrderId = :id")
    Optional<Integer> findCrmQuoteId(@Param("tenant") String tenant, @Param("id") String id);

    @Query(value = "select nextval('erp_order_seq')", nativeQuery = true)
    long nextOrderNumber();

    /** Insert-if-absent on (tenant, CRM quote): a concurrent identical request inserts nothing and replays the winner's order. */
    @Modifying(flushAutomatically = true)
    @Query(value = """
            INSERT INTO sales_orders (erp_order_id, tenant_id, crm_quote_id, erp_customer_id, total, status, correlation_id)
            VALUES (:id, :tenant, :quote, :customer, :total, 'Open', :corr)
            ON CONFLICT (tenant_id, crm_quote_id) DO NOTHING""", nativeQuery = true)
    int insertIfAbsent(@Param("id") String id, @Param("tenant") String tenant, @Param("quote") int quote,
                       @Param("customer") String customer, @Param("total") java.math.BigDecimal total, @Param("corr") String corr);

    @Modifying(flushAutomatically = true)
    @Query(value = """
            INSERT INTO order_inbox (tenant_id, correlation_id, crm_quote_id, erp_order_id)
            VALUES (:tenant, :corr, :quote, :order) ON CONFLICT (tenant_id, correlation_id) DO NOTHING""", nativeQuery = true)
    int recordInbox(@Param("tenant") String tenant, @Param("corr") String corr, @Param("quote") int quote, @Param("order") String order);
}
