# Sales <-> fulfilment bot negotiation

Advisory only: nothing is written to the CRM or the ERP.

    POST /api/v1/negotiations            (CRM, crm.write)  -> starts Java BotNegotiationWorkflow
    GET  /api/v1/negotiations/{id}       (CRM, crm.write)  -> { status, transcript } via the workflow queries

- **Workflow** (Java, `temporal/workflows`, queue `orchestration`): alternates the bots and enforces `NegotiationRules` on every message.
- **Sales bot** (C#, `Crm.Api`, activity `crm.sales-bot.take-turn`, queue `crm`): `SalesBotActivities` -> `ISalesBrain`.
- **Fulfilment bot** (Java, ERP, activity `erp.fulfilment-bot.take-turn`, queue `erp`).
- `Bots:Mode=rules` (default): deterministic `SalesGuardrail`. `Bots:Mode=maf`: Foundgine MAF agent (`AddFoundgineAgentFramework`) with the guardrail as safety net; register an `IChatClient` in `Program.cs`. Compose: `BOTS_MODE=maf`.
- The activity carries no token: tenant comes from the workflow input and the agent's Foundgine data tools fail closed there.
- e2e: `E2E_NEGOTIATION=1` adds a negotiation step to `e2e/run.sh` (off by default).
- Contracts: `contracts/schemas/bot-*.v1.json`; Java: `CrossLanguageContractTest`; C#: `SalesBotTests`.
