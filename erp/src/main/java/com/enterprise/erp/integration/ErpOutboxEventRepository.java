package com.enterprise.erp.integration;

import java.util.Collection;
import java.util.List;
import java.util.Optional;
import java.util.UUID;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

/**
 * The outbox is a queue with database-side leasing, so its state changes stay native statements: they depend on the
 * database clock (now()), on ON CONFLICT and on FOR UPDATE SKIP LOCKED, none of which JPQL can express, and an entity
 * round trip would use the application clock instead. All @Modifying methods need a transaction; the callers have one.
 */
public interface ErpOutboxEventRepository extends JpaRepository<ErpOutboxEvent, UUID> {

    /**
     * Insert-if-absent on (tenant, event type, entity, version): a duplicate append adds nothing. Returns rows inserted
     * (0 = the event already existed). A JPA save() would raise a unique violation instead and abort the transaction
     * that also carries the domain write.
     */
    @Modifying(flushAutomatically = true)
    @Query(value = """
            INSERT INTO erp_temporal_event_outbox
                (event_id, tenant_id, crm_quote_id, erp_order_id, event_type, entity_id, entity_version, payload)
            VALUES (:id, :tenant, :quote, :order, :type, :entity, :version, CAST(:payload AS jsonb))
            ON CONFLICT (tenant_id, event_type, entity_id, entity_version) DO NOTHING""", nativeQuery = true)
    int insertIfAbsent(@Param("id") UUID id, @Param("tenant") String tenant, @Param("quote") int quote,
                       @Param("order") String order, @Param("type") String type, @Param("entity") String entity,
                       @Param("version") int version, @Param("payload") String payload);

    @Query("select e.eventId from ErpOutboxEvent e where e.tenantId = :tenant and e.eventType = :type "
         + "and e.entityId = :entity and e.entityVersion = :version")
    Optional<UUID> findEventId(@Param("tenant") String tenant, @Param("type") String type,
                               @Param("entity") String entity, @Param("version") int version);

    /**
     * Locks up to 50 due events (not published, available, not leased) in creation order, skipping rows another relay
     * instance has locked. Columns: event_id, tenant_id, crm_quote_id, event_type, payload (as text). The locks last until
     * the caller's transaction ends, which is why {@link #lease} runs in the same one.
     */
    @Query(value = """
            SELECT event_id, tenant_id, crm_quote_id, event_type, CAST(payload AS text)
            FROM erp_temporal_event_outbox
            WHERE published_at IS NULL AND available_at <= now()
              AND (locked_until IS NULL OR locked_until < now())
            ORDER BY created_at
            LIMIT 50
            FOR UPDATE SKIP LOCKED""", nativeQuery = true)
    List<Object[]> lockDue();

    /** Leases the events for 30 seconds and counts the attempt. */
    @Modifying(flushAutomatically = true)
    @Query(value = "UPDATE erp_temporal_event_outbox SET locked_until = now() + interval '30 seconds', attempts = attempts + 1 "
                 + "WHERE event_id IN (:ids)", nativeQuery = true)
    int lease(@Param("ids") Collection<UUID> ids);

    @Modifying(flushAutomatically = true)
    @Query(value = "UPDATE erp_temporal_event_outbox SET published_at = now(), locked_until = NULL, last_error = NULL "
                 + "WHERE event_id = :id", nativeQuery = true)
    int markPublished(@Param("id") UUID id);

    /** Releases the lease and retries after min(300, 2^min(attempts, 8)) seconds (attempts already counts this try). */
    @Modifying(flushAutomatically = true)
    @Query(value = """
            UPDATE erp_temporal_event_outbox
            SET locked_until = NULL,
                available_at = now() + (LEAST(300, CAST(POWER(2, LEAST(attempts, 8)) AS integer)) * interval '1 second'),
                last_error = :error
            WHERE event_id = :id""", nativeQuery = true)
    int markFailed(@Param("id") UUID id, @Param("error") String error);
}
