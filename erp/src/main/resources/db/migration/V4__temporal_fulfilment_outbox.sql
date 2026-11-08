-- Transactional ERP -> CRM workflow event relay. Domain writes and this outbox row share one DB transaction.
CREATE TABLE erp_temporal_event_outbox (
    event_id uuid PRIMARY KEY,
    tenant_id varchar(64) NOT NULL,
    crm_quote_id integer NOT NULL,
    erp_order_id varchar(64) NOT NULL,
    event_type varchar(32) NOT NULL CHECK (event_type IN ('shipment', 'invoice', 'payment')),
    entity_id varchar(64) NOT NULL,
    entity_version integer NOT NULL CHECK (entity_version > 0),
    payload jsonb NOT NULL,
    attempts integer NOT NULL DEFAULT 0,
    available_at timestamptz NOT NULL DEFAULT now(),
    locked_until timestamptz,
    published_at timestamptz,
    last_error varchar(1000),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_erp_temporal_event_version UNIQUE (tenant_id, event_type, entity_id, entity_version)
);
CREATE INDEX ix_erp_temporal_event_pending ON erp_temporal_event_outbox (available_at, created_at) WHERE published_at IS NULL;
