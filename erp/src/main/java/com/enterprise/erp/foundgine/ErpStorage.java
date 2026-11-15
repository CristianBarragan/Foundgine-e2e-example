package com.enterprise.erp.foundgine;

import com.enterprise.erp.customer.ErpCustomer;
import com.enterprise.erp.fulfilment.ErpInvoice;
import com.enterprise.erp.fulfilment.ErpPayment;
import com.enterprise.erp.fulfilment.ErpShipment;
import com.enterprise.erp.order.SalesOrder;
import com.enterprise.erp.returns.ErpReturn;
import com.foundgine.core.semantic.configuration.FoundginePersistenceBuilder;
import com.foundgine.core.semantic.configuration.FoundginePersistenceConfiguration;

/**
 * Storage side: tables, columns, keys, indexes and checks. Mirrors the Flyway migrations in db/migration, which stay
 * the owner of the real schema (Hibernate only validates). Keep the two in step; do not generate a second migration.
 */
final class ErpStorage {
    private ErpStorage() {}

    static final class CustomerStorage extends FoundginePersistenceConfiguration<ErpCustomer> {
        @Override
        public void configure(FoundginePersistenceBuilder<ErpCustomer> model) {
            model.toTable("erp_customers");
            model.hasKey(ErpCustomer::getErpCustomerId);
            model.property(ErpCustomer::getErpCustomerId).hasColumnName("erp_customer_id").hasMaxLength(64).isRequired();
            model.property(ErpCustomer::getTenantId).hasColumnName("tenant_id").hasMaxLength(64).isRequired();
            model.property(ErpCustomer::getCrmCustomerId).hasColumnName("crm_customer_id").isRequired();
            model.property(ErpCustomer::getKind).hasColumnName("kind").hasMaxLength(16).isRequired();
            model.property(ErpCustomer::getName).hasColumnName("name").hasMaxLength(200).isRequired();
            model.property(ErpCustomer::getEmail).hasColumnName("email").hasMaxLength(320).isRequired();
            model.property(ErpCustomer::getPhone).hasColumnName("phone").hasMaxLength(50).isRequired();
            model.property(ErpCustomer::getStatus).hasColumnName("status").hasMaxLength(16).isRequired();
            model.hasUniqueIndex("ux_erp_customers_tenant_crm", ErpCustomer::getTenantId, ErpCustomer::getCrmCustomerId);
            model.hasCheck("ck_erp_customers_kind", "kind IN ('Business', 'Individual')");
            model.hasCheck("ck_erp_customers_status", "status IN ('Active', 'Inactive')");
        }
    }

    static final class OrderStorage extends FoundginePersistenceConfiguration<SalesOrder> {
        @Override
        public void configure(FoundginePersistenceBuilder<SalesOrder> model) {
            model.toTable("sales_orders");
            model.hasKey(SalesOrder::getErpOrderId);
            model.property(SalesOrder::getErpOrderId).hasColumnName("erp_order_id").hasMaxLength(64).isRequired();
            model.property(SalesOrder::getTenantId).hasColumnName("tenant_id").hasMaxLength(64).isRequired();
            model.property(SalesOrder::getCrmQuoteId).hasColumnName("crm_quote_id").isRequired();
            model.property(SalesOrder::getErpCustomerId).hasColumnName("erp_customer_id").hasMaxLength(64).isRequired();
            model.property(SalesOrder::getTotal).hasColumnName("total").hasPrecision(14, 2).isRequired();
            model.property(SalesOrder::getStatus).hasColumnName("status").hasMaxLength(16).isRequired();
            model.property(SalesOrder::getCancelReason).hasColumnName("cancel_reason").hasMaxLength(500).isRequired();
            model.property(SalesOrder::getCorrelationId).hasColumnName("correlation_id").hasMaxLength(64).isRequired();
            model.property(SalesOrder::getUpdatedAt).hasColumnName("updated_at").isRequired();
            model.hasUniqueIndex("ux_sales_orders_tenant_quote", SalesOrder::getTenantId, SalesOrder::getCrmQuoteId);
            model.hasCheck("ck_sales_orders_status", "status IN ('Open', 'Cancelled')");
        }
    }

    static final class ReturnStorage extends FoundginePersistenceConfiguration<ErpReturn> {
        @Override
        public void configure(FoundginePersistenceBuilder<ErpReturn> model) {
            model.toTable("erp_returns");
            model.hasKey(ErpReturn::getErpReturnId);
            model.property(ErpReturn::getErpReturnId).hasColumnName("erp_return_id").hasMaxLength(64).isRequired();
            model.property(ErpReturn::getTenantId).hasColumnName("tenant_id").hasMaxLength(64).isRequired();
            model.property(ErpReturn::getCrmReturnId).hasColumnName("crm_return_id").isRequired();
            model.property(ErpReturn::getErpOrderId).hasColumnName("erp_order_id").hasMaxLength(64).isRequired();
            model.property(ErpReturn::getStatus).hasColumnName("status").hasMaxLength(16).isRequired();
            model.property(ErpReturn::getReason).hasColumnName("reason").hasMaxLength(500).isRequired();
            model.property(ErpReturn::getNote).hasColumnName("note").hasMaxLength(500).isRequired();
            model.property(ErpReturn::getCorrelationId).hasColumnName("correlation_id").hasMaxLength(64).isRequired();
            model.property(ErpReturn::getUpdatedAt).hasColumnName("updated_at").isRequired();
            model.hasUniqueIndex("ux_erp_returns_tenant_crm", ErpReturn::getTenantId, ErpReturn::getCrmReturnId);
            model.hasIndex("ix_erp_returns_order", ErpReturn::getTenantId, ErpReturn::getErpOrderId);
            model.hasCheck("ck_erp_returns_status", "status IN ('Authorized', 'Rejected', 'Completed')");
        }
    }

    static final class ShipmentStorage extends FoundginePersistenceConfiguration<ErpShipment> {
        @Override
        public void configure(FoundginePersistenceBuilder<ErpShipment> model) {
            model.toTable("erp_shipments");
            model.hasKey(ErpShipment::getErpShipmentId);
            model.property(ErpShipment::getErpShipmentId).hasColumnName("erp_shipment_id").hasMaxLength(64).isRequired();
            model.property(ErpShipment::getTenantId).hasColumnName("tenant_id").hasMaxLength(64).isRequired();
            model.property(ErpShipment::getErpOrderId).hasColumnName("erp_order_id").hasMaxLength(64).isRequired();
            model.property(ErpShipment::getStatus).hasColumnName("status").hasMaxLength(16).isRequired();
            model.property(ErpShipment::getCarrier).hasColumnName("carrier").hasMaxLength(120);
            model.property(ErpShipment::getTrackingNumber).hasColumnName("tracking_number").hasMaxLength(120);
            model.property(ErpShipment::getIdempotencyKey).hasColumnName("idempotency_key").hasMaxLength(128);
            model.property(ErpShipment::getVersion).hasColumnName("version").isRequired();
            model.property(ErpShipment::getUpdatedAt).hasColumnName("updated_at").isRequired();
            model.hasIndex("ix_erp_shipments_order", ErpShipment::getTenantId, ErpShipment::getErpOrderId);
            model.hasCheck("ck_erp_shipments_status", "status IN ('Packed','Shipped','InTransit','Delivered','Cancelled')");
            model.hasCheck("ck_erp_shipments_version", "version > 0");
        }
    }

    static final class InvoiceStorage extends FoundginePersistenceConfiguration<ErpInvoice> {
        @Override
        public void configure(FoundginePersistenceBuilder<ErpInvoice> model) {
            model.toTable("erp_invoices");
            model.hasKey(ErpInvoice::getErpInvoiceId);
            model.property(ErpInvoice::getErpInvoiceId).hasColumnName("erp_invoice_id").hasMaxLength(64).isRequired();
            model.property(ErpInvoice::getTenantId).hasColumnName("tenant_id").hasMaxLength(64).isRequired();
            model.property(ErpInvoice::getErpOrderId).hasColumnName("erp_order_id").hasMaxLength(64).isRequired();
            model.property(ErpInvoice::getInvoiceNumber).hasColumnName("invoice_number").hasMaxLength(64).isRequired();
            model.property(ErpInvoice::getTotal).hasColumnName("total").hasPrecision(14, 2).isRequired();
            model.property(ErpInvoice::getStatus).hasColumnName("status").hasMaxLength(16).isRequired();
            model.property(ErpInvoice::getIssuedAt).hasColumnName("issued_at").isRequired();
            model.property(ErpInvoice::getDueAt).hasColumnName("due_at");
            model.property(ErpInvoice::getVersion).hasColumnName("version").isRequired();
            model.property(ErpInvoice::getUpdatedAt).hasColumnName("updated_at").isRequired();
            model.hasUniqueIndex("ux_erp_invoice_tenant_number", ErpInvoice::getTenantId, ErpInvoice::getInvoiceNumber);
            model.hasUniqueIndex("ux_erp_invoice_order", ErpInvoice::getTenantId, ErpInvoice::getErpOrderId);
            model.hasCheck("ck_erp_invoices_status", "status IN ('Issued','PartiallyPaid','Paid','Overdue','Void')");
            model.hasCheck("ck_erp_invoices_total", "total >= 0");
        }
    }

    static final class PaymentStorage extends FoundginePersistenceConfiguration<ErpPayment> {
        @Override
        public void configure(FoundginePersistenceBuilder<ErpPayment> model) {
            model.toTable("erp_payments");
            model.hasKey(ErpPayment::getErpPaymentId);
            model.property(ErpPayment::getErpPaymentId).hasColumnName("erp_payment_id").hasMaxLength(64).isRequired();
            model.property(ErpPayment::getTenantId).hasColumnName("tenant_id").hasMaxLength(64).isRequired();
            model.property(ErpPayment::getErpOrderId).hasColumnName("erp_order_id").hasMaxLength(64).isRequired();
            model.property(ErpPayment::getErpInvoiceId).hasColumnName("erp_invoice_id").hasMaxLength(64).isRequired();
            model.property(ErpPayment::getAmount).hasColumnName("amount").hasPrecision(14, 2).isRequired();
            model.property(ErpPayment::getStatus).hasColumnName("status").hasMaxLength(16).isRequired();
            model.property(ErpPayment::getIdempotencyKey).hasColumnName("idempotency_key").hasMaxLength(128);
            model.property(ErpPayment::getVersion).hasColumnName("version").isRequired();
            model.property(ErpPayment::getUpdatedAt).hasColumnName("updated_at").isRequired();
            model.hasIndex("ix_erp_payments_invoice", ErpPayment::getTenantId, ErpPayment::getErpInvoiceId);
            model.hasCheck("ck_erp_payments_status", "status IN ('Received','Reconciled','Reversed')");
            model.hasCheck("ck_erp_payments_amount", "amount > 0");
        }
    }
}
