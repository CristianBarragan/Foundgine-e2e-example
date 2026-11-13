package com.enterprise.erp.fulfilment;

import com.enterprise.erp.persistence.AssignedIdEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.Instant;
import java.util.UUID;

/**
 * Whole-order shipment. {@code version} is bumped by hand on every status change (not @Version): its value is part of the
 * event contract the Temporal workflows apply monotonically.
 */
@Entity
@Table(name = "erp_shipments")
public class ErpShipment extends AssignedIdEntity {
    @Id @Column(name = "erp_shipment_id") private String erpShipmentId;
    @Column(name = "tenant_id") private String tenantId;
    @Column(name = "erp_order_id") private String erpOrderId;
    @Column(name = "status") private String status;
    @Column(name = "carrier") private String carrier;
    @Column(name = "tracking_number") private String trackingNumber;
    @Column(name = "idempotency_key") private String idempotencyKey;
    @Column(name = "version") private int version;
    // created_at and the insert-time updated_at come from the column defaults, so they are not written on insert.
    @Column(name = "updated_at", insertable = false) private Instant updatedAt;

    protected ErpShipment() {}

    public static ErpShipment packed(String tenantId, String erpOrderId, String carrier, String trackingNumber, String idempotencyKey) {
        var s = new ErpShipment();
        s.erpShipmentId = "SHP-" + UUID.randomUUID();
        s.tenantId = tenantId; s.erpOrderId = erpOrderId; s.status = "Packed";
        s.carrier = carrier; s.trackingNumber = trackingNumber; s.idempotencyKey = idempotencyKey; s.version = 1;
        return s;
    }

    @Override public String getId() { return erpShipmentId; }
    public String getErpShipmentId() { return erpShipmentId; }
    public String getErpOrderId() { return erpOrderId; }
    public String getStatus() { return status; }
    public String getCarrier() { return carrier; }
    public String getTrackingNumber() { return trackingNumber; }
    public int getVersion() { return version; }

    public void moveTo(String next) { this.status = next; this.version++; this.updatedAt = Instant.now(); }
}
