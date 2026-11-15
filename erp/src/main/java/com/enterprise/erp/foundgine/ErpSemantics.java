package com.enterprise.erp.foundgine;

import com.enterprise.erp.customer.ErpCustomer;
import com.enterprise.erp.fulfilment.ErpInvoice;
import com.enterprise.erp.fulfilment.ErpPayment;
import com.enterprise.erp.fulfilment.ErpShipment;
import com.enterprise.erp.order.SalesOrder;
import com.enterprise.erp.returns.ErpReturn;
import com.foundgine.core.semantic.configuration.FieldNaming;
import com.foundgine.core.semantic.configuration.FoundgineModelBuilder;
import com.foundgine.core.semantic.configuration.FoundgineModelConfiguration;

/**
 * Semantic side: entity names, aliases, dimensions and relationships. Says nothing about storage. The ERP's JPA
 * classes double as the semantic models (the CRM's identity-map approach), so relationships are declared by name.
 */
final class ErpSemantics {
    private ErpSemantics() {}

    static final class CustomerSemantics extends FoundgineModelConfiguration<ErpCustomer> {
        @Override
        public void configure(FoundgineModelBuilder<ErpCustomer> model) {
            model.toEntity("Customer");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasAlias("Account", 90);
            model.hasAlias("Client", 85);
            model.hasDimension(ErpCustomer::getTenantId).as("tenant");
            model.hasDimension(ErpCustomer::getStatus).as("status");
            model.hasDimension(ErpCustomer::getKind).as("kind");
            model.hasMany("orders", SalesOrder.class);
        }
    }

    static final class OrderSemantics extends FoundgineModelConfiguration<SalesOrder> {
        @Override
        public void configure(FoundgineModelBuilder<SalesOrder> model) {
            model.toEntity("SalesOrder");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasAlias("Order", 95);
            model.hasDimension(SalesOrder::getTenantId).as("tenant");
            model.hasDimension(SalesOrder::getStatus).as("status");
            model.hasOne("customer", ErpCustomer.class);
            model.hasMany("shipments", ErpShipment.class);
            model.hasMany("invoices", ErpInvoice.class);
            model.hasMany("payments", ErpPayment.class);
            model.hasMany("returns", ErpReturn.class);
        }
    }

    static final class ReturnSemantics extends FoundgineModelConfiguration<ErpReturn> {
        @Override
        public void configure(FoundgineModelBuilder<ErpReturn> model) {
            model.toEntity("Return");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasAlias("RMA", 90);
            model.hasDimension(ErpReturn::getTenantId).as("tenant");
            model.hasDimension(ErpReturn::getStatus).as("status");
            model.hasOne("order", SalesOrder.class);
        }
    }

    static final class ShipmentSemantics extends FoundgineModelConfiguration<ErpShipment> {
        @Override
        public void configure(FoundgineModelBuilder<ErpShipment> model) {
            model.toEntity("Shipment");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasAlias("Delivery", 85);
            model.hasDimension(ErpShipment::getTenantId).as("tenant");
            model.hasDimension(ErpShipment::getStatus).as("status");
            model.hasDimension(ErpShipment::getCarrier).as("carrier");
            model.hasOne("order", SalesOrder.class);
        }
    }

    static final class InvoiceSemantics extends FoundgineModelConfiguration<ErpInvoice> {
        @Override
        public void configure(FoundgineModelBuilder<ErpInvoice> model) {
            model.toEntity("Invoice");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasAlias("Bill", 85);
            model.hasDimension(ErpInvoice::getTenantId).as("tenant");
            model.hasDimension(ErpInvoice::getStatus).as("status");
            model.hasOne("order", SalesOrder.class);
            model.hasMany("payments", ErpPayment.class);
        }
    }

    static final class PaymentSemantics extends FoundgineModelConfiguration<ErpPayment> {
        @Override
        public void configure(FoundgineModelBuilder<ErpPayment> model) {
            model.toEntity("Payment");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasDimension(ErpPayment::getTenantId).as("tenant");
            model.hasDimension(ErpPayment::getStatus).as("status");
            model.hasOne("order", SalesOrder.class);
            model.hasOne("invoice", ErpInvoice.class);
        }
    }
}
