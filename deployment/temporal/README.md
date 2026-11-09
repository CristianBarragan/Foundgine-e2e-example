# Temporal deployment

For local development, install the Temporal CLI and run `temporal server start-dev`. This uses the local development service and an in-memory database; it is not a production deployment.

For production, deploy Temporal Server or Temporal Cloud separately from application workers. Configure:

- durable supported persistence (or Temporal Cloud), backups and disaster recovery;
- TLS and authenticated client/worker connections;
- namespace and task-queue isolation by environment;
- worker autoscaling, graceful shutdown and resource limits;
- payload encryption/codecs and retention policy;
- metrics, tracing, alerting and workflow visibility;
- tested upgrade, workflow versioning and rollback procedures.

Do not expose the Temporal frontend or workflow starter API publicly. The starter API must sit behind the enterprise gateway and Keycloak authorization.
