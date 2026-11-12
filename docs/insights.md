# Cross-entity scenarios (insights)

Read-only, tenant-scoped endpoints under `/api/v1` (policy `crm.read`). Code: `Application/InsightsService.cs`.
Two tools, each used where it is strongest:

* **Relationship questions** are Foundgine semantic reads built with lambda filters (`Application/Querying`):
  `crm.From<Customer>().Eq(c => c.Status, "Active").Where(Filter<Customer>.Some(c => c.Opportunities, ...))`.
  `CrmReader` always starts from the caller's tenant; the read still goes through `FoundgineEfReader`.
* **Date arithmetic, grouping and money** are EF Core LINQ over the same `FoundgineDbContext`
  (the semantic filter set is only Eq/Neq/In/And/Or plus relationship quantifiers and COUNT).

| Endpoint | Question it answers | Tool |
|---|---|---|
| `GET /insights/at-risk-customers` | Active customers with an open opportunity, no Accepted/Ordered quote, and at least one Rejected/OrderFailed quote | semantic (`Some` / `None` / `Some`) |
| `GET /insights/key-accounts?minOrders=3` | Active customers with at least N Ordered quotes | semantic (`AtLeast`, a COUNT filter) |
| `GET /insights/integration-attention` | Customers whose ERP mapping is Pending or Failed (their quotes cannot be accepted) | semantic (`Some` over `IntegrationMappings`) |
| `GET /insights/stale-opportunities?days=30` | Open opportunities untouched for N days whose quotes are equally quiet (max 100) | EF |
| `GET /insights/pipeline` | Count and amount per stage, open pipeline, won amount, win rate (won / closed) | EF |
| `GET /insights/receivables` | Per customer: invoice total minus Received/Reconciled payments, aged Current / 1-30 / 31-60 / 60+ days past due | EF |
| `GET /customers/{id}/360` | Customer, ERP link, contacts, opportunities, quotes with lines, shipments, invoices, payments, returns, invoiced / paid / outstanding | EF |

Rules worth knowing: void invoices and Paid invoices are excluded from receivables; reversed payments do not count as
paid; `Outstanding` never goes negative; another tenant's customer is `404` on `/360`. Semantic rows come back as
dictionaries keyed by field name, entity-backed results as the entity.

`Filter<T>` builds core IR only: `Eq`, `Neq`, `In`, `And`, `Or`, `Some`, `None`, `AtLeast`. Field and relationship
ids come from the CLR property names, exactly as in `CrmSemantics`.
