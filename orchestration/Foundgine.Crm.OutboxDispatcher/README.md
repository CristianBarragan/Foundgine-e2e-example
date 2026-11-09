# CRM outbox dispatcher (Foundgine Temporal bridge)

CRM-owned host. `CommercialOrderService` enqueues a `CommercialOrderAccepted` message into `foundgine_outbox`
(Foundgine.Providers `PostgresOutbox`) inside the order-acceptance transaction. This host runs
`AddFoundgineGatewayPostgresOutbox()` + `AddFoundgineTemporal(...).RouteTopic(...).DispatchOutboxToTemporal()`:
the Foundgine dispatcher leases messages, `FoundgineTemporalOutboxPublisher` starts the process, and retry/backoff
and dead-lettering (`dead_lettered_at`) are handled by the Foundgine dispatcher.

- Apply `applications/crm-dotnet/backend.Migrations/Migrations/Outbox.sql` (the `foundgine_outbox` schema).
- Message id = CRM order id; workflow id = `foundgine/{tenant}/commercial-order-fulfillment/{orderId:N}`.
- Config: `ConnectionStrings__Crm` (or `CRM_DATABASE_URL`), `TEMPORAL_ADDRESS`, `TEMPORAL_NAMESPACE`,
  `TEMPORAL_TASK_QUEUE` (must match the worker), `TEMPORAL_USE_TLS`, `TEMPORAL_API_KEY`.
- Unrouted topics fail (retry then dead-letter) rather than being dropped.
- Dead-lettered rows need an operator replay path (not built here).
