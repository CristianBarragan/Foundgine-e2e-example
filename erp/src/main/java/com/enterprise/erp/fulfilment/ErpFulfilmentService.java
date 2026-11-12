package com.enterprise.erp.fulfilment;

import com.enterprise.erp.customer.ValidationFailedException;
import com.enterprise.erp.integration.ErpTemporalEventOutbox;
import java.math.BigDecimal;
import java.time.Instant;
import java.time.temporal.ChronoUnit;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/** ERP-owned shipment, invoice and payment lifecycle. Domain writes and outbox events commit atomically. */
@Service
public class ErpFulfilmentService {
    private final JdbcClient jdbc;
    private final ErpTemporalEventOutbox outbox;
    public ErpFulfilmentService(JdbcClient jdbc, ErpTemporalEventOutbox outbox) { this.jdbc = jdbc; this.outbox = outbox; }

    @Transactional
    public ShipmentView createShipment(String tenant, String orderId, CreateShipment in) {
        requireOpenOrder(tenant, orderId);
        if (in == null || blank(in.carrier()) || blank(in.trackingNumber()) || blank(in.idempotencyKey()) || in.idempotencyKey().length()>128)
            throw invalid("carrier, trackingNumber and an Idempotency-Key (max 128 chars) are required.");
        var existing = jdbc.sql("SELECT erp_shipment_id,erp_order_id,carrier,tracking_number FROM erp_shipments WHERE tenant_id=:tenant AND idempotency_key=:key")
            .param("tenant",tenant).param("key",in.idempotencyKey())
            .query((rs,n)->new String[]{rs.getString(1),rs.getString(2),rs.getString(3),rs.getString(4)}).optional();
        if (existing.isPresent()) {
            if (!orderId.equals(existing.get()[1]) ||
                    !in.carrier().trim().equals(existing.get()[2]) ||
                    !in.trackingNumber().trim().equals(existing.get()[3]))
                throw invalid("Idempotency-Key was already used with a different shipment command.");
            return shipment(tenant,existing.get()[0]);
        }
        String id = "SHP-" + UUID.randomUUID();
        jdbc.sql("INSERT INTO erp_shipments(erp_shipment_id,tenant_id,erp_order_id,status,carrier,tracking_number,idempotency_key) VALUES(:id,:tenant,:order,'Packed',:carrier,:tracking,:key)")
            .param("id",id).param("tenant",tenant).param("order",orderId).param("carrier",in.carrier().trim()).param("tracking",in.trackingNumber().trim()).param("key",in.idempotencyKey()).update();
        ShipmentView view = shipment(tenant,id); publish(tenant,view); return view;
    }

    @Transactional
    public ShipmentView transitionShipment(String tenant, String id, String next) {
        ShipmentView old = jdbc.sql("SELECT erp_shipment_id,erp_order_id,status,carrier,tracking_number,version FROM erp_shipments WHERE tenant_id=:tenant AND erp_shipment_id=:id FOR UPDATE")
            .param("tenant",tenant).param("id",id).query((rs,n)->new ShipmentView(rs.getString(1),rs.getString(2),rs.getString(3),rs.getString(4),rs.getString(5),rs.getInt(6))).optional()
            .orElseThrow(()->invalid("Shipment not found for this tenant."));
        // A retried transition after a lost HTTP response is a successful no-op.
        if (old.status().equals(next)) return old;
        if (!allowed(old.status(),next)) throw invalid("Invalid shipment transition " + old.status() + " -> " + next + ".");
        ShipmentView updated = new ShipmentView(id,old.erpOrderId(),next,old.carrier(),old.trackingNumber(),old.version()+1);
        jdbc.sql("UPDATE erp_shipments SET status=:status,version=:version,updated_at=now() WHERE tenant_id=:tenant AND erp_shipment_id=:id")
            .param("status",next).param("version",updated.version()).param("tenant",tenant).param("id",id).update();
        publish(tenant,updated); return updated;
    }

    @Transactional
    public InvoiceView issueInvoice(String tenant, String orderId, IssueInvoice in) {
        var order = jdbc.sql("SELECT total,status FROM sales_orders WHERE tenant_id=:tenant AND erp_order_id=:order FOR UPDATE")
            .param("tenant",tenant).param("order",orderId).query((rs,n)->new OrderTotal(rs.getBigDecimal(1),rs.getString(2))).optional()
            .orElseThrow(()->invalid("Order not found for this tenant."));
        if (!"Open".equals(order.status())) throw invalid("Only open orders can be invoiced.");
        var priorInvoice = jdbc.sql("SELECT erp_invoice_id FROM erp_invoices WHERE tenant_id=:tenant AND erp_order_id=:order")
            .param("tenant",tenant).param("order",orderId).query(String.class).optional();
        if (priorInvoice.isPresent()) return invoice(tenant,priorInvoice.get());
        Instant issued = in != null && in.issuedAt()!=null ? in.issuedAt() : Instant.now();
        Instant due = in != null && in.dueAt()!=null ? in.dueAt() : issued.plus(30,ChronoUnit.DAYS);
        if (!due.isAfter(issued)) throw invalid("Invoice dueAt must be after issuedAt.");
        String id="INV-"+UUID.randomUUID();
        String number=in!=null && !blank(in.invoiceNumber()) ? in.invoiceNumber().trim() : "INV-"+UUID.randomUUID().toString().substring(0,8).toUpperCase();
        jdbc.sql("INSERT INTO erp_invoices(erp_invoice_id,tenant_id,erp_order_id,invoice_number,total,status,issued_at,due_at) VALUES(:id,:tenant,:order,:number,:total,'Issued',:issued,:due)")
            .param("id",id).param("tenant",tenant).param("order",orderId).param("number",number).param("total",order.total()).param("issued",issued).param("due",due).update();
        InvoiceView view=invoice(tenant,id); publish(tenant,view); return view;
    }

    @Transactional
    public PaymentView receivePayment(String tenant, String invoiceId, ReceivePayment in) {
        if (in==null || in.amount()==null || in.amount().signum()<=0) throw invalid("A payment amount greater than zero is required.");
        var invoice=jdbc.sql("SELECT erp_order_id,total,status FROM erp_invoices WHERE tenant_id=:tenant AND erp_invoice_id=:id FOR UPDATE")
            .param("tenant",tenant).param("id",invoiceId).query((rs,n)->new InvoiceBalance(rs.getString(1),rs.getBigDecimal(2),rs.getString(3))).optional()
            .orElseThrow(()->invalid("Invoice not found for this tenant."));
        if (blank(in.idempotencyKey()) || in.idempotencyKey().length()>128) throw invalid("An Idempotency-Key (max 128 chars) is required.");
        var existing = jdbc.sql("SELECT erp_payment_id,erp_invoice_id,amount FROM erp_payments WHERE tenant_id=:tenant AND idempotency_key=:key")
            .param("tenant",tenant).param("key",in.idempotencyKey())
            .query((rs,n)->new Object[]{rs.getString(1),rs.getString(2),rs.getBigDecimal(3)}).optional();
        if (existing.isPresent()) {
            if (!invoiceId.equals(existing.get()[1]) || in.amount().compareTo((BigDecimal) existing.get()[2]) != 0)
                throw invalid("Idempotency-Key was already used with a different payment command.");
            return payment(tenant,(String) existing.get()[0]);
        }
        if (Set.of("Paid","Void").contains(invoice.status())) throw invalid("Payments cannot be added to a paid or void invoice.");
        BigDecimal committed = jdbc.sql("SELECT COALESCE(sum(amount),0) FROM erp_payments WHERE tenant_id=:tenant AND erp_invoice_id=:id AND status IN ('Received','Reconciled')")
            .param("tenant",tenant).param("id",invoiceId).query(BigDecimal.class).single();
        if (in.amount().compareTo(invoice.total().subtract(committed))>0) throw invalid("Payment exceeds the outstanding invoice balance.");
        String id="PAY-"+UUID.randomUUID();
        jdbc.sql("INSERT INTO erp_payments(erp_payment_id,tenant_id,erp_order_id,erp_invoice_id,amount,status,idempotency_key) VALUES(:id,:tenant,:order,:invoice,:amount,'Received',:key)")
            .param("id",id).param("tenant",tenant).param("order",invoice.erpOrderId()).param("invoice",invoiceId).param("amount",in.amount()).param("key",in.idempotencyKey()).update();
        PaymentView view=payment(tenant,id); publish(tenant,view); return view;
    }

    @Transactional
    public PaymentView reconcilePayment(String tenant, String paymentId) {
        PaymentView old=jdbc.sql("SELECT erp_payment_id,erp_order_id,erp_invoice_id,amount,status,version FROM erp_payments WHERE tenant_id=:tenant AND erp_payment_id=:id FOR UPDATE")
            .param("tenant",tenant).param("id",paymentId).query((rs,n)->new PaymentView(rs.getString(1),rs.getString(2),rs.getString(3),rs.getBigDecimal(4),rs.getString(5),rs.getInt(6))).optional()
            .orElseThrow(()->invalid("Payment not found for this tenant."));
        if ("Reconciled".equals(old.status())) return old;
        if (!"Received".equals(old.status())) throw invalid("Only received payments can be reconciled.");
        PaymentView updated=new PaymentView(paymentId,old.erpOrderId(),old.erpInvoiceId(),old.amount(),"Reconciled",old.version()+1);
        jdbc.sql("UPDATE erp_payments SET status='Reconciled',version=:version,updated_at=now() WHERE tenant_id=:tenant AND erp_payment_id=:id")
            .param("version",updated.version()).param("tenant",tenant).param("id",paymentId).update();
        var inv=jdbc.sql("SELECT total,status,version FROM erp_invoices WHERE tenant_id=:tenant AND erp_invoice_id=:id FOR UPDATE")
            .param("tenant",tenant).param("id",old.erpInvoiceId()).query((rs,n)->new InvoiceState(rs.getBigDecimal(1),rs.getString(2),rs.getInt(3))).single();
        BigDecimal paid=reconciledAmount(tenant,old.erpInvoiceId());
        if (!"Void".equals(inv.status())) {
            String status=paid.compareTo(inv.total())>=0 ? "Paid" : "PartiallyPaid";
            if (!status.equals(inv.status())) {
                jdbc.sql("UPDATE erp_invoices SET status=:status,version=:version,updated_at=now() WHERE tenant_id=:tenant AND erp_invoice_id=:id")
                    .param("status",status).param("version",inv.version()+1).param("tenant",tenant).param("id",old.erpInvoiceId()).update();
                publish(tenant,invoice(tenant,old.erpInvoiceId()));
            }
        }
        publish(tenant,updated); return updated;
    }

    @Transactional
    public PaymentView reversePayment(String tenant, String paymentId) {
        PaymentView old=jdbc.sql("SELECT erp_payment_id,erp_order_id,erp_invoice_id,amount,status,version FROM erp_payments WHERE tenant_id=:tenant AND erp_payment_id=:id FOR UPDATE")
            .param("tenant",tenant).param("id",paymentId).query((rs,n)->new PaymentView(rs.getString(1),rs.getString(2),rs.getString(3),rs.getBigDecimal(4),rs.getString(5),rs.getInt(6))).optional()
            .orElseThrow(()->invalid("Payment not found for this tenant."));
        if ("Reversed".equals(old.status())) return old;
        if (!Set.of("Received","Reconciled").contains(old.status())) throw invalid("Only received or reconciled payments can be reversed.");
        PaymentView updated=new PaymentView(paymentId,old.erpOrderId(),old.erpInvoiceId(),old.amount(),"Reversed",old.version()+1);
        jdbc.sql("UPDATE erp_payments SET status='Reversed',version=:version,updated_at=now() WHERE tenant_id=:tenant AND erp_payment_id=:id")
            .param("version",updated.version()).param("tenant",tenant).param("id",paymentId).update();
        var inv=jdbc.sql("SELECT total,status,version FROM erp_invoices WHERE tenant_id=:tenant AND erp_invoice_id=:id FOR UPDATE")
            .param("tenant",tenant).param("id",old.erpInvoiceId()).query((rs,n)->new InvoiceState(rs.getBigDecimal(1),rs.getString(2),rs.getInt(3))).single();
        BigDecimal paid=reconciledAmount(tenant,old.erpInvoiceId());
        String status=paid.signum()==0 ? "Issued" : paid.compareTo(inv.total())>=0 ? "Paid" : "PartiallyPaid";
        if (!"Void".equals(inv.status()) && !status.equals(inv.status())) {
            jdbc.sql("UPDATE erp_invoices SET status=:status,version=:version,updated_at=now() WHERE tenant_id=:tenant AND erp_invoice_id=:id")
                .param("status",status).param("version",inv.version()+1).param("tenant",tenant).param("id",old.erpInvoiceId()).update();
            publish(tenant,invoice(tenant,old.erpInvoiceId()));
        }
        publish(tenant,updated); return updated;
    }

    private void publish(String tenant,ShipmentView x){outbox.shipment(new ErpTemporalEventOutbox.ShipmentEvent(tenant,x.erpOrderId(),x.erpShipmentId(),x.status(),x.carrier(),x.trackingNumber(),x.version()));}
    private void publish(String tenant,InvoiceView x){outbox.invoice(new ErpTemporalEventOutbox.InvoiceEvent(tenant,x.erpOrderId(),x.erpInvoiceId(),x.invoiceNumber(),x.total(),x.status(),x.issuedAt(),x.dueAt(),x.version()));}
    private void publish(String tenant,PaymentView x){outbox.payment(new ErpTemporalEventOutbox.PaymentEvent(tenant,x.erpOrderId(),x.erpInvoiceId(),x.erpPaymentId(),x.amount(),x.status(),x.version()));}
    private ShipmentView shipment(String t,String id){return jdbc.sql("SELECT erp_shipment_id,erp_order_id,status,carrier,tracking_number,version FROM erp_shipments WHERE tenant_id=:tenant AND erp_shipment_id=:id").param("tenant",t).param("id",id).query((rs,n)->new ShipmentView(rs.getString(1),rs.getString(2),rs.getString(3),rs.getString(4),rs.getString(5),rs.getInt(6))).single();}
    private InvoiceView invoice(String t,String id){return jdbc.sql("SELECT erp_invoice_id,erp_order_id,invoice_number,total,status,issued_at,due_at,version FROM erp_invoices WHERE tenant_id=:tenant AND erp_invoice_id=:id").param("tenant",t).param("id",id).query((rs,n)->new InvoiceView(rs.getString(1),rs.getString(2),rs.getString(3),rs.getBigDecimal(4),rs.getString(5),rs.getTimestamp(6).toInstant(),rs.getTimestamp(7)==null?null:rs.getTimestamp(7).toInstant(),rs.getInt(8))).single();}
    private PaymentView payment(String t,String id){return jdbc.sql("SELECT erp_payment_id,erp_order_id,erp_invoice_id,amount,status,version FROM erp_payments WHERE tenant_id=:tenant AND erp_payment_id=:id").param("tenant",t).param("id",id).query((rs,n)->new PaymentView(rs.getString(1),rs.getString(2),rs.getString(3),rs.getBigDecimal(4),rs.getString(5),rs.getInt(6))).single();}
    private BigDecimal reconciledAmount(String t,String id){return jdbc.sql("SELECT COALESCE(sum(amount),0) FROM erp_payments WHERE tenant_id=:tenant AND erp_invoice_id=:id AND status='Reconciled'").param("tenant",t).param("id",id).query(BigDecimal.class).single();}
    private void requireOpenOrder(String t,String id){if(jdbc.sql("SELECT count(*) FROM sales_orders WHERE tenant_id=:tenant AND erp_order_id=:id AND status='Open'").param("tenant",t).param("id",id).query(Integer.class).single()!=1)throw invalid("Open order not found for this tenant.");}
    private static boolean allowed(String from,String to){if(to==null)return false;return switch(from){case "Packed"->Set.of("Shipped","Cancelled").contains(to);case "Shipped"->Set.of("InTransit","Delivered","Cancelled").contains(to);case "InTransit"->Set.of("Delivered","Cancelled").contains(to);default->false;};}
    private static boolean blank(String s){return s==null||s.isBlank();}
    private static ValidationFailedException invalid(String m){return new ValidationFailedException(List.of(m));}
    private record OrderTotal(BigDecimal total,String status){}
    private record InvoiceBalance(String erpOrderId,BigDecimal total,String status){}
    private record InvoiceState(BigDecimal total,String status,int version){}
    public record CreateShipment(String carrier,String trackingNumber,String idempotencyKey){}
    public record IssueInvoice(String invoiceNumber,Instant issuedAt,Instant dueAt){}
    public record ReceivePayment(BigDecimal amount,String idempotencyKey){}
    public record ShipmentView(String erpShipmentId,String erpOrderId,String status,String carrier,String trackingNumber,int version){}
    public record InvoiceView(String erpInvoiceId,String erpOrderId,String invoiceNumber,BigDecimal total,String status,Instant issuedAt,Instant dueAt,int version){}
    public record PaymentView(String erpPaymentId,String erpOrderId,String erpInvoiceId,BigDecimal amount,String status,int version){}
}
