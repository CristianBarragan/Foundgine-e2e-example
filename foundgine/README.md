# Foundgine — Shared Semantic Execution Boundary

## Responsibility

Foundgine is the common semantic execution boundary for enterprise consumers. It provides:

- semantic metadata and capability discovery;
- query intent resolution and bounded query planning;
- mutation planning and dry-run;
- policy evaluation and approval gates;
- tenant-aware execution context;
- provider/adapter dispatch;
- MCP, REST and GraphQL transport adapters;
- audit records, correlation and observability.

Foundgine is not a business system of record. It must not own CRM customer records or ERP stock,
supplier, purchase-order or fulfillment records.

## Current source reality

The CRM project currently hosts Foundgine in-process through NuGet packages. The Java ERP
currently hosts Foundgine in-process through Maven artifacts. Both applications therefore retain
their own runtime process and domain boundary.

A separate enterprise Foundgine service/gateway is **not present** in the supplied archives.
The directories here define the intended shared platform responsibilities and adapter contracts;
they are not a fabricated runnable service.

## Recommended execution model

External callers (UI, agents, Microsoft Foundry, Agent Framework and MCP clients) should use the
enterprise semantic boundary. The boundary authenticates the caller, resolves the capability,
applies policy, and dispatches to the owning application adapter. The adapter then invokes the
application's own supported API or MCP operation. The owner re-checks authorization at its
boundary; central policy never replaces domain-level enforcement.

For high-risk writes:
1. resolve and validate intent;
2. produce a deterministic plan and dry-run result;
3. require the relevant approval;
4. execute using an idempotency key;
5. record actor, tenant, policy decision, plan fingerprint and outcome;
6. reconcile asynchronous completion through events.

## Compatibility gate

The supplied applications use Foundgine 2.3.1 (.NET and Java). Before claiming a unified
platform compatibility level, validate the cross-language contract and runtime behavior against
a pinned compatibility matrix. Do not assume matching semantics solely from similar package names.
