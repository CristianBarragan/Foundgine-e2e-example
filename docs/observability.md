# Observability (CRM side)

Switch on with `Telemetry:Enabled=true` and the standard `OTEL_EXPORTER_OTLP_ENDPOINT` (and `OTEL_SERVICE_NAME` if you want
to override `crm-api`). Nothing is exported when disabled.

**Traces:** ASP.NET Core and HttpClient instrumentation plus Temporal's `TracingInterceptor` on the client (workflow start,
from the outbox publisher), on the Foundgine client in `Crm.Api` (workflow start/status/signal through `/api/v1/workflows`
and the outbox bridge; added by `AddFoundgineTemporalTracing`, because Foundgine has no interceptor option) and on the CRM
worker (activities). Not covered: the `Crm.Worker` process (Foundgine process worker) and the Java ERP client and workers. The trace context travels in Temporal headers, so a single
trace spans API request -> outbox publish -> workflow -> CRM activity. Every request span carries `correlation.id`
(from `X-Correlation-Id`, also in log scopes and `admin_audit`). The Java side must install the equivalent Temporal OTel
interceptor to join the same trace.

**Metrics** (meter `Crm.Api`): `crm.outbox.published`, `crm.outbox.publish_failed` (tag `type`), `crm.admin.audit`
(tags `action`, `outcome`), plus ASP.NET Core request metrics.

**Suggested alerts** (not wired to an alert manager yet):
- Outbox lag: `SELECT count(*), min(created_at) FROM outbox_messages WHERE published_at IS NULL` - alert if the oldest unpublished
  message is older than 5 minutes, or `crm.outbox.publish_failed` rate > 0 for 10 minutes.
- Customers stuck syncing: `customer_integration_mappings` with `status = 'Failed'`, or `Pending` older than 15 minutes.
- Returns stuck: `return_requests` in `Requested` older than 1 hour.
- HTTP 429 and 5xx rate from the ASP.NET Core metrics.
