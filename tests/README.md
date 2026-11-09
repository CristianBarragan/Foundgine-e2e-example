# Enterprise verification plan

The source archives contain application-level tests. This directory defines the missing
cross-application verification suites to add as integration is implemented.

- `integration`: schema compatibility, adapter contract tests, event ordering and replay.
- `security`: token issuer/audience validation, role mapping, approval enforcement and denial.
- `tenant-isolation`: cross-tenant reads, references, event routing and replay attempts.
- `end-to-end`: commercial order → ERP reservation → CRM status projection; rejection and release.

Do not treat the proposed JSON Schemas as proof of runtime event publication. Add producer and
consumer contract tests when event handlers are implemented.
