# Increment 1 (static fixes, NOT yet built or run)
No .NET SDK, Maven registry, NuGet feed or Docker was reachable where these changes were made, so nothing here was compiled or run.
1. `orchestration.Dockerfile` and the README expect `temporal/workflows`, but the module was inside `erp/src/main/resources/workflows` -> moved it to `temporal/workflows` (the Docker build of `orchestration` could not have worked, and the ERP jar was packaging its sources as resources).
2. CI: `surefire-reportsreports` typo fixed; added an `orchestration` job (`mvn -f temporal/workflows/pom.xml verify`).
3. Foundgine packages moved from 3.3.3 to 3.3.5 via one property (`crm/Directory.Build.props`); `scripts/pack-foundgine.sh` packs from the repo into `nuget-local` when nuget.org lacks a version. Old 3.3.2/3.3.3 nupkgs left in place.
4. CRM / SQL Server: the unique e-mail index (`ux_customers_tenant_email`) was never created (it was a Postgres `lower(email)` index that the generated migration dropped), so duplicate e-mails were never rejected. Added as a plain unique index (SQL Server's default collation is case-insensitive) in `CrmStorage`, the migration, its Designer and the snapshot.
Next: run `dotnet build`, `dotnet ef migrations has-pending-model-changes`, `dotnet test crm/Crm.slnx` and `scripts/demo.sh`, and send the first failing output.

## Increment 2
5. Line endings: scripts and config had CRLF endings, so `bash` failed on `e2e/run.sh`, `start.sh`, `scripts/*.sh` (`set: pipefail\r: invalid option name`) - the e2e container could never have run. All text files normalised to LF; `.gitattributes` now forces LF for .sh/.py/.sql/Dockerfile/.yml.
Verified here: `scripts/validate-contracts.py` passes; `e2e/selftest/run-selftest.sh` passes (mock stack, no Docker).
Still unverified: .NET build/tests, EF migration, Maven builds, Docker demo (NuGet and Maven Central are unreachable from this sandbox).

## Increment 3 (MAF provider + Java Temporal worker) - NOT built or run
6. The Java `BotNegotiationWorkflow` calls activity `crm.sales-bot.take-turn` on queue `crm`, but no C# implementation existed. Added in `crm/src/Crm.Api/Integration/`:
   `SalesBotContracts.cs` (DTOs mirroring `contracts/schemas/bot-*.v1.json`), `SalesBot.cs` (`SalesGuardrail` pure rules, `RuleSalesBrain` default, `MafSalesBrain` on a Foundgine MAF agent, `SalesBotActivities`, `AddCrmSalesBot`), `BotNegotiationStarter.cs`.
   The activity is registered on the CRM's hosted worker; `POST /api/v1/negotiations` and `GET /api/v1/negotiations/{id}` start and read the Java workflow.
7. `Bots:Mode` = `rules` (default, no model) or `maf` (uses `AddFoundgineAgentFramework`; the host must register an `IChatClient`). The guardrail clamps any model reply to the customer's limits; the activity has no bearer token, so Foundgine data tools fail closed there.
8. `Crm.Api.csproj` references `Foundgine.Providers.AgentFramework` (`$(FoundgineVersion)`). It is post-3.3.5 in the Foundgine repo (unreleased), so run `scripts/pack-foundgine.sh <Foundgine repo> 3.3.5` first to place it in `nuget-local`, otherwise restore fails.
9. Tests added: `crm/tests/Crm.Api.Tests/SalesBotTests.cs`, and `CrossLanguageContractTest.theCSharpSalesBotReplyIsAValidNegotiationMessage` on the Java side.
Not done: an e2e step for the negotiation in `e2e/run.sh`, compose env for `Bots__Mode`, docs page. Nothing in this increment has been compiled; API names of `Microsoft.Agents.AI` and Temporalio (`ApplicationFailureException` ctor, `QueryAsync`) are from memory and may need small fixes.

## Increment 4
10. `scripts/demo.sh` packs the MAF package when `FOUNDGINE_REPO` is set (fails with a clear message otherwise); CI `crm` job packs Foundgine from the repo; compose passes `Bots__Mode` (`BOTS_MODE`, default rules); optional `E2E_NEGOTIATION=1` step in `e2e/run.sh`; `docs/bot-negotiation.md`.
