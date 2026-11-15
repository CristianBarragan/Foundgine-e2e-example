package com.enterprise.erp.foundgine;

import com.foundgine.core.semantic.configuration.FoundgineModel;
import com.foundgine.core.semantic.configuration.FoundgineModelRegistrar;

/**
 * Single registration point for the ERP's Foundgine model, in the same shape as the supply-chain sample and the CRM:
 * semantic side, storage side, entity maps, relationship connections. {@link #build()} validates the whole set and
 * yields the {@code MetadataRegistry} every Foundgine provider (planner, ORM, MCP, GraphQL, migrations) reads.
 *
 * <p>Scope: the six single-key entities (customer, order, return, shipment, invoice, payment). Foundgine entities have
 * one key column, so the composite-key tables (erp_products, erp_inventory, sales_order_lines, stock_reservations,
 * erp_return_lines) are not modelled here; the ERP keeps serving them through its own JPA repositories.
 */
public final class ErpFoundgineModel extends FoundgineModel {
    @Override
    public void configure(FoundgineModelRegistrar model) {
        model.configure(ErpSemantics.CustomerSemantics.class);
        model.configure(ErpSemantics.OrderSemantics.class);
        model.configure(ErpSemantics.ReturnSemantics.class);
        model.configure(ErpSemantics.ShipmentSemantics.class);
        model.configure(ErpSemantics.InvoiceSemantics.class);
        model.configure(ErpSemantics.PaymentSemantics.class);

        model.configure(ErpStorage.CustomerStorage.class);
        model.configure(ErpStorage.OrderStorage.class);
        model.configure(ErpStorage.ReturnStorage.class);
        model.configure(ErpStorage.ShipmentStorage.class);
        model.configure(ErpStorage.InvoiceStorage.class);
        model.configure(ErpStorage.PaymentStorage.class);

        model.configure(ErpEntityMaps.CustomerMap.class);
        model.configure(ErpEntityMaps.OrderMap.class);
        model.configure(ErpEntityMaps.ReturnMap.class);
        model.configure(ErpEntityMaps.ShipmentMap.class);
        model.configure(ErpEntityMaps.InvoiceMap.class);
        model.configure(ErpEntityMaps.PaymentMap.class);

        model.configure(ErpConnections.class);
    }
}
