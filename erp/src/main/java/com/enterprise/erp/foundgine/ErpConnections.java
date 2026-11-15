package com.enterprise.erp.foundgine;

import com.enterprise.erp.customer.ErpCustomer;
import com.enterprise.erp.fulfilment.ErpInvoice;
import com.enterprise.erp.fulfilment.ErpPayment;
import com.enterprise.erp.fulfilment.ErpShipment;
import com.enterprise.erp.order.SalesOrder;
import com.enterprise.erp.returns.ErpReturn;
import com.foundgine.core.semantic.configuration.FoundgineConnectionBuilder;
import com.foundgine.core.semantic.configuration.FoundgineConnectionConfiguration;

/** Physical connections: which keys join each pair of related entities (the foreign keys in the Flyway schema). */
final class ErpConnections extends FoundgineConnectionConfiguration {
    @Override
    public void configure(FoundgineConnectionBuilder connections) {
        connections.hasRelationship(ErpCustomer.class, SalesOrder.class)
                .using(ErpCustomer.class, SalesOrder.class)
                .hasForeignKey(SalesOrder::getErpCustomerId)
                .references(ErpCustomer::getErpCustomerId);

        connections.hasRelationship(SalesOrder.class, ErpShipment.class)
                .using(SalesOrder.class, ErpShipment.class)
                .hasForeignKey(ErpShipment::getErpOrderId)
                .references(SalesOrder::getErpOrderId);

        connections.hasRelationship(SalesOrder.class, ErpInvoice.class)
                .using(SalesOrder.class, ErpInvoice.class)
                .hasForeignKey(ErpInvoice::getErpOrderId)
                .references(SalesOrder::getErpOrderId);

        connections.hasRelationship(SalesOrder.class, ErpPayment.class)
                .using(SalesOrder.class, ErpPayment.class)
                .hasForeignKey(ErpPayment::getErpOrderId)
                .references(SalesOrder::getErpOrderId);

        connections.hasRelationship(SalesOrder.class, ErpReturn.class)
                .using(SalesOrder.class, ErpReturn.class)
                .hasForeignKey(ErpReturn::getErpOrderId)
                .references(SalesOrder::getErpOrderId);

        connections.hasRelationship(ErpInvoice.class, ErpPayment.class)
                .using(ErpInvoice.class, ErpPayment.class)
                .hasForeignKey(ErpPayment::getErpInvoiceId)
                .references(ErpInvoice::getErpInvoiceId);
    }
}
