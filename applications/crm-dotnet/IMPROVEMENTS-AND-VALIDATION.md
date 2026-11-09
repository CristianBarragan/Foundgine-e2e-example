# Improvements and validation status

## Changes in this revision

- Added a floating AI chat panel to the React CRM UI.
- Added `POST /api/assistant/chat`, backed by a configurable OpenAI-compatible Chat Completions endpoint.
- Restricted the model to one read-only function, `crm_query`. It delegates to the existing `FoundgineMcpTools.ExecuteQueryAsync` path; it receives no SQL, database connection, tenant, subject, or write tool.
- Preserved the authenticated Keycloak request context for the assistant route so Foundgine's runtime policy and warrant provider resolve the same caller/tenant as normal MCP and semantic REST calls.
- Added message/tool-round/result bounds, safe upstream error responses, a 20-requests-per-minute per-subject rate limit, and an API-only key configuration.
- Added a second Keycloak demo user in a separate tenant with a read-only role.
- Added Playwright E2E coverage for unauthenticated API rejection, browser login, tenant-isolated company creation/read, read-only write denial, and (when configured) the AI-to-Foundgine query path.
- Added an isolated PowerShell E2E runner and aligned local Keycloak/database defaults with Docker Compose.
- Corrected documentation to distinguish the API's 2.2.2 NuGet references from older 2.2.1 companion migration notes.

## Static checks completed here

- ZIP archive integrity and source extraction.
- JSON parsing for `appsettings.json`, the Keycloak realm import, and `package.json`.
- YAML parsing for `docker-compose.yml`.
- TypeScript/TSX syntax transpilation for the app, entry point, Playwright config, and E2E spec.

## Not executed / not claimed

This environment does not contain the .NET SDK or Docker, and the frontend dependency installation did not complete. Therefore this archive has **not** been verified by `dotnet restore`, `dotnet build`, `dotnet test`, EF migration execution, `docker compose up`, browser tests, a live Keycloak login, PostgreSQL execution, an actual AI provider call, or Native AOT publish. Run `verification/run-e2e.ps1` on a machine with Docker Desktop, .NET 10-capable container builds, Node.js 20+, and Playwright Chromium to perform the full-stack gates.

## Remaining hardening before production

- Replace the in-memory RSA warrant authority and replay store with shared KMS-backed signing and replay protection for multi-replica deployments.
- Split CRM and Keycloak database roles/credentials; do not use the demo passwords or shared database master account.
- Configure HTTPS, a fixed trusted Keycloak issuer, secret management, backups, monitoring, and rate limits appropriate to the deployment.
- Reconcile the 2.2.1 source/migration notes with the 2.2.2 NuGet references by building against the exact Foundgine release intended for deployment.
- Add schema-parity checks between the EF migration model and Foundgine AOT metadata, and verify Native AOT separately.
- Add write operations to the assistant only after explicit host-side tenant-bound mutation authorization, confirmation UX, audit evidence, and end-to-end tests exist. The current assistant intentionally cannot write.
