package com.enterprise.erp.integration;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.Instant;
import java.util.UUID;

/**
 * A row of the transactional ERP -> CRM event outbox. Rows are written with {@link ErpOutboxEventRepository#insertIfAbsent}
 * and leased/settled by the repository's native statements, never saved through this entity.
 *
 * <p>The {@code payload} jsonb column is deliberately NOT mapped: it is written by the native insert (CAST to jsonb) and read
 * as text by the relay's claim query. Mapping a String to jsonb behaves differently across Hibernate versions, and a
 * mismatch would fail schema validation for the whole application.
 */
@Entity
@Table(name = "erp_temporal_event_outbox")
public class ErpOutboxEvent {
    @Id @Column(name = "event_id") private UUID eventId;
    @Column(name = "tenant_id") private String tenantId;
    @Column(name = "crm_quote_id") private int crmQuoteId;
    @Column(name = "erp_order_id") private String erpOrderId;
    @Column(name = "event_type") private String eventType;
    @Column(name = "entity_id") private String entityId;
    @Column(name = "entity_version") private int entityVersion;
    @Column(name = "attempts") private int attempts;
    @Column(name = "available_at") private Instant availableAt;
    @Column(name = "locked_until") private Instant lockedUntil;
    @Column(name = "published_at") private Instant publishedAt;
    @Column(name = "last_error") private String lastError;

    protected ErpOutboxEvent() {}

    public UUID getEventId() { return eventId; }
    public String getTenantId() { return tenantId; }
    public int getCrmQuoteId() { return crmQuoteId; }
    public String getEventType() { return eventType; }
    public int getAttempts() { return attempts; }
    public Instant getPublishedAt() { return publishedAt; }
    public String getLastError() { return lastError; }
}
