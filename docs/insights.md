# Cross-entity scenarios (insights)

Read-only, tenant-scoped endpoints under `/api/v1` (policy `crm.read`). Code: `Application/InsightsService.cs`.
The report endpoints use EF Core LINQ over the same `FoundgineDbContext`. Relationship predicates are expressed as
correlated `Any` / `Count` subqueries, with an explicit tenant predicate on both the customer and every related
entity. This keeps the generated SQL in the relational provider's supported query shape and makes tenant isolation
visible at each join boundary. Date arithmetic, grouping and money calculations use the same EF Core context.

| Endpoint | Question it answers | Tool |
|---|---|---|
| `GET /insights/at-risk-customers` | Active customers with an open opportunity, no Accepted/Ordered quote, and at least one Rejected/OrderFailed quote | EF (`Any` / `!Any`) |
| `GET /insights/key-accounts?minOrders=3` | Active customers with at least N Ordered quotes | EF (`Count`) |
| `GET /insights/integration-attention` | Customers whose ERP mapping is Pending or Failed (their quotes cannot be accepted) | EF (`Any`) |
| `GET /insights/stale-opportunities?days=30` | Open opportunities untouched for N days whose quotes are equally quiet (max 100) | EF |
| `GET /insights/pipeline` | Count and amount per stage, open pipeline, won amount, win rate (won / closed) | EF |
| `GET /insights/receivables` | Per customer: invoice total minus Received/Reconciled payments, aged Current / 1-30 / 31-60 / 60+ days past due | EF |
| `GET /customers/{id}/360` | Customer, ERP link, contacts, opportunities, quotes with lines, shipments, invoices, payments, returns, invoiced / paid / outstanding | EF |

Rules worth knowing: void invoices and Paid invoices are excluded from receivables; reversed payments do not count as
paid; `Outstanding` never goes negative; another tenant's customer is `404` on `/360`. The three customer insight
endpoints return dictionaries keyed by the customer field names; entity-backed results are returned as entities.

`Filter<T>` remains available for other Foundgine semantic reads and builds core IR only: `Eq`, `Neq`, `In`, `And`,
`Or`, `Some`, `None`, `AtLeast`. Field and relationship ids come from CLR property names, exactly as in
`CrmSemantics`.
