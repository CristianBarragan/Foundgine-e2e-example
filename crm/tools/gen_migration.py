#!/usr/bin/env python3
"""Generates the EF migration (Up/Down), its Designer and the model snapshot from one schema spec, so the three can
never disagree. The spec is the original SQL schema (ground truth) expressed as EF model facts."""
import sys, os
out = sys.argv[1]
ID = "20261113000000_InitialCrm"
NS = "Crm.Api.Infrastructure.Migrations"
CTX = "FoundgineDbContext"

def vc(n): return f"character varying({n})"
T_INT, T_LONG, T_TS, T_BOOL, T_UUID, T_TEXT, T_JSONB = "integer", "bigint", "timestamp with time zone", "boolean", "uuid", "text", "jsonb"
def num(): return "numeric(14,2)"

class P:  # property
    def __init__(s, name, clr, col, sql, nullable=False, maxlen=None, identity=False, never=False, default_sql=None, prec=None):
        s.name, s.clr, s.col, s.sql, s.nullable, s.maxlen, s.identity, s.never, s.default_sql, s.prec = name, clr, col, sql, nullable, maxlen, identity, never, default_sql, prec

def S(name, col, n, nullable=False): return P(name, "string", col, vc(n), nullable, n)
def I(name, col, nullable=False): return P(name, "int", col, T_INT, nullable)
def D(name, col, nullable=False): return P(name, "DateTime", col, T_TS, nullable)
def M(name, col): return P(name, "decimal", col, num(), prec=(14, 2))

TENANT = lambda: S("TenantId", "tenant_id", 64)
def AUD(): return [D("CreatedAt", "created_at"), S("CreatedBy", "created_by", 128), D("UpdatedAt", "updated_at"), S("UpdatedBy", "updated_by", 128)]

E = []  # entities in dependency order
def ent(cls, table, props, pk, pkname, idx=(), checks=(), fks=()):
    E.append(dict(cls=cls, table=table, props=props, pk=pk, pkname=pkname, idx=list(idx), checks=list(checks), fks=list(fks)))

ent("Customer", "customers",
    [P("Id", "int", "customer_id", T_INT, identity=True), TENANT(), S("Kind", "kind", 16), S("Name", "name", 200), S("Email", "email", 320),
     S("Phone", "phone", 50), S("Status", "status", 16)] + AUD(),
    ["Id"], "customers_pkey",
    idx=[("ix_customers_tenant_name", ["TenantId", "Name"], False, None, None)],
    checks=[("customers_kind_check", "kind IN ('Business', 'Individual')"), ("customers_status_check", "status IN ('Active', 'Inactive')")])

ent("Contact", "contacts",
    [P("Id", "int", "contact_id", T_INT, identity=True), TENANT(), I("CustomerId", "customer_id"), S("FirstName", "first_name", 100),
     S("LastName", "last_name", 100), S("Email", "email", 320), S("Phone", "phone", 50), P("IsPrimary", "bool", "is_primary", T_BOOL)] + AUD(),
    ["Id"], "contacts_pkey",
    idx=[("ix_contacts_tenant_customer", ["TenantId", "CustomerId"], False, None, None)],
    fks=[("CustomerId", "Customer", "contacts_customer_id_fkey", "customer_id")])

ent("CustomerIntegrationMapping", "customer_integration_mappings",
    [P("CrmCustomerId", "int", "crm_customer_id", T_INT, never=True), TENANT(), S("ErpCustomerId", "erp_customer_id", 64, True),
     S("Status", "status", 16), I("Attempt", "attempt"), P("LastError", "string", "last_error", T_TEXT, True),
     D("CreatedAt", "created_at"), D("UpdatedAt", "updated_at")],
    ["CrmCustomerId"], "customer_integration_mappings_pkey",
    idx=[("ux_mappings_tenant_erp", ["TenantId", "ErpCustomerId"], True, "erp_customer_id IS NOT NULL", None)],
    checks=[("customer_integration_mappings_status_check", "status IN ('Pending', 'Active', 'Failed')")],
    fks=[("CrmCustomerId", "Customer", "customer_integration_mappings_crm_customer_id_fkey", "customer_id")])

ent("Opportunity", "opportunities",
    [P("Id", "int", "opportunity_id", T_INT, identity=True), TENANT(), I("CustomerId", "customer_id"), S("Name", "name", 200),
     S("Stage", "stage", 20), M("Amount", "amount")] + AUD(),
    ["Id"], "opportunities_pkey",
    idx=[("ix_opportunities_tenant_customer", ["TenantId", "CustomerId"], False, None, None)],
    checks=[("opportunities_stage_check", "stage IN ('Prospecting','Qualification','Proposal','Negotiation','ClosedWon','ClosedLost')")],
    fks=[("CustomerId", "Customer", "opportunities_customer_id_fkey", "customer_id")])

ent("Quote", "quotes",
    [P("Id", "int", "quote_id", T_INT, identity=True), TENANT(), I("OpportunityId", "opportunity_id"), I("CustomerId", "customer_id"),
     S("Status", "status", 20), M("Total", "total"), S("SubmittedBy", "submitted_by", 128), S("DecidedBy", "decided_by", 128),
     S("ErpOrderId", "erp_order_id", 64), S("OrderFailureReason", "order_failure_reason", 500)] + AUD(),
    ["Id"], "quotes_pkey",
    idx=[("ix_quotes_tenant_customer", ["TenantId", "CustomerId"], False, None, None),
         ("ux_quotes_tenant_erp_order", ["TenantId", "ErpOrderId"], True, "erp_order_id <> ''", None)],
    checks=[("quotes_status_check", "status IN ('Draft','PendingApproval','Approved','Rejected','Accepted','Ordered','OrderFailed')")],
    fks=[("OpportunityId", "Opportunity", "quotes_opportunity_id_fkey", "opportunity_id"), ("CustomerId", "Customer", "quotes_customer_id_fkey", "customer_id")])

ent("QuoteLine", "quote_lines",
    [P("Id", "int", "quote_line_id", T_INT, identity=True), TENANT(), I("QuoteId", "quote_id"), S("Sku", "sku", 64), S("Description", "description", 300),
     I("Quantity", "quantity"), M("UnitPrice", "unit_price")],
    ["Id"], "quote_lines_pkey",
    idx=[("ix_quote_lines_quote", ["TenantId", "QuoteId"], False, None, None)],
    checks=[("quote_lines_quantity_check", "quantity > 0"), ("quote_lines_unit_price_check", "unit_price >= 0")],
    fks=[("QuoteId", "Quote", "quote_lines_quote_id_fkey", "quote_id")])

ent("ReturnRequest", "return_requests",
    [P("Id", "int", "return_id", T_INT, identity=True), TENANT(), I("QuoteId", "quote_id"), I("CustomerId", "customer_id"), S("Status", "status", 20),
     S("Reason", "reason", 500), S("ErpReturnId", "erp_return_id", 64), S("DecisionNote", "decision_note", 500), S("RequestedBy", "requested_by", 128),
     D("CreatedAt", "created_at"), D("UpdatedAt", "updated_at")],
    ["Id"], "return_requests_pkey",
    idx=[("ix_returns_quote", ["TenantId", "QuoteId"], False, None, None), ("ux_returns_tenant_erp", ["TenantId", "ErpReturnId"], True, "erp_return_id <> ''", None)],
    checks=[("return_requests_status_check", "status IN ('Requested','Authorized','Rejected','Completed')")],
    fks=[("QuoteId", "Quote", "return_requests_quote_id_fkey", "quote_id"), ("CustomerId", "Customer", "return_requests_customer_id_fkey", "customer_id")])

ent("ReturnRequestLine", "return_request_lines",
    [P("Id", "int", "return_line_id", T_INT, identity=True), TENANT(), I("ReturnId", "return_id"), S("Sku", "sku", 64), I("Quantity", "quantity")],
    ["Id"], "return_request_lines_pkey",
    idx=[("ix_return_lines_return", ["TenantId", "ReturnId"], False, None, None)],
    checks=[("return_request_lines_quantity_check", "quantity > 0")],
    fks=[("ReturnId", "ReturnRequest", "return_request_lines_return_id_fkey", "return_id")])

ent("Shipment", "shipments",
    [TENANT(), S("ErpShipmentId", "erp_shipment_id", 64), I("QuoteId", "quote_id"), S("ErpOrderId", "erp_order_id", 64), S("Status", "status", 20),
     S("Carrier", "carrier", 100), S("TrackingNumber", "tracking_number", 100), I("Version", "version"), D("UpdatedAt", "updated_at")],
    ["TenantId", "ErpShipmentId"], "shipments_pkey",
    idx=[("ix_shipments_quote", ["TenantId", "QuoteId"], False, None, None)],
    checks=[("shipments_status_check", "status IN ('Packed','Shipped','InTransit','Delivered','Cancelled')"), ("shipments_version_check", "version > 0")],
    fks=[("QuoteId", "Quote", "shipments_quote_id_fkey", "quote_id")])

ent("Invoice", "invoices",
    [TENANT(), S("ErpInvoiceId", "erp_invoice_id", 64), I("QuoteId", "quote_id"), I("CustomerId", "customer_id"), S("ErpOrderId", "erp_order_id", 64),
     S("Number", "number", 64), M("Total", "total"), S("Status", "status", 20), D("IssuedAt", "issued_at"), D("DueAt", "due_at", True),
     I("Version", "version"), D("UpdatedAt", "updated_at")],
    ["TenantId", "ErpInvoiceId"], "invoices_pkey",
    idx=[("ix_invoices_quote", ["TenantId", "QuoteId"], False, None, None), ("ix_invoices_customer", ["TenantId", "CustomerId"], False, None, None)],
    checks=[("invoices_total_check", "total >= 0"), ("invoices_status_check", "status IN ('Issued','PartiallyPaid','Paid','Overdue','Void')"), ("invoices_version_check", "version > 0")],
    fks=[("QuoteId", "Quote", "invoices_quote_id_fkey", "quote_id"), ("CustomerId", "Customer", "invoices_customer_id_fkey", "customer_id")])

ent("Payment", "payments",
    [TENANT(), S("ErpPaymentId", "erp_payment_id", 64), S("ErpInvoiceId", "erp_invoice_id", 64), I("QuoteId", "quote_id"), M("Amount", "amount"),
     S("Status", "status", 20), I("Version", "version"), D("UpdatedAt", "updated_at")],
    ["TenantId", "ErpPaymentId"], "payments_pkey",
    idx=[("ix_payments_quote", ["TenantId", "QuoteId"], False, None, None)],
    checks=[("payments_amount_check", "amount >= 0"), ("payments_status_check", "status IN ('Received','Reconciled','Reversed')"), ("payments_version_check", "version > 0")],
    fks=[("QuoteId", "Quote", "payments_quote_id_fkey", "quote_id")])

ent("OutboxMessage", "outbox_messages",
    [P("Id", "Guid", "id", T_UUID, never=True), TENANT(), S("Type", "type", 100), I("AggregateId", "aggregate_id"), I("Attempt", "attempt"),
     P("Payload", "string", "payload", T_JSONB), D("CreatedAt", "created_at"), D("PublishedAt", "published_at", True), I("PublishAttempts", "publish_attempts"),
     P("NextAttemptAt", "DateTime", "next_attempt_at", T_TS, default_sql="now()"), P("LastError", "string", "last_error", T_TEXT, True)],
    ["Id"], "outbox_messages_pkey",
    idx=[("ix_outbox_unpublished", ["NextAttemptAt"], False, "published_at IS NULL", None)])

ent("AdminAuditEntry", "admin_audit",
    [P("Id", "long", "audit_id", T_LONG, identity=True), TENANT(), S("UserId", "user_id", 128), S("Action", "action", 64), S("Target", "target", 128),
     S("Outcome", "outcome", 16), S("Detail", "detail", 300), S("CorrelationId", "correlation_id", 64), D("OccurredAt", "occurred_at")],
    ["Id"], "admin_audit_pkey",
    idx=[("ix_admin_audit_tenant_time", ["TenantId", "OccurredAt"], False, None, [False, True])],
    checks=[("admin_audit_outcome_check", "outcome IN ('Succeeded','Rejected','Failed')")])

def q(s): return s.replace("\\", "\\\\").replace('"', '\\"')
def col_of(e, prop): return next(p for p in e["props"] if p.name == prop).col
def cs_bool(b): return "true" if b else "false"

# ---- migration (Up/Down) -------------------------------------------------------------------------------------------
def column_line(p):
    args = [f'type: "{p.sql}"']
    if p.maxlen: args.append(f"maxLength: {p.maxlen}")
    if p.prec: args.append(f"precision: {p.prec[0]}m"); args.append(f"scale: {p.prec[1]}m")
    args.append(f"nullable: {cs_bool(p.nullable)}")
    if p.default_sql: args.append(f'defaultValueSql: "{p.default_sql}"')
    clr = p.clr + ("?" if p.nullable and p.clr not in ("string",) else "")
    line = f"{p.col} = table.Column<{clr}>({', '.join(args)})"
    if p.identity: line += '\n                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)'
    return line

up = []
for e in E:
    cols = ",\n                    ".join(column_line(p) for p in e["props"])
    cons = []
    pk = e["pk"]
    expr = f"x => x.{col_of(e, pk[0])}" if len(pk) == 1 else "x => new { " + ", ".join(f"x.{col_of(e, k)}" for k in pk) + " }"
    cons.append(f'table.PrimaryKey("{e["pkname"]}", {expr});')
    for prop, principal, name, pcol in e["fks"]:
        pt = next(x for x in E if x["cls"] == principal)["table"]
        cons.append(f'table.ForeignKey(name: "{name}", column: x => x.{col_of(e, prop)}, principalTable: "{pt}", principalColumn: "{pcol}");')
    for name, sql in e["checks"]:
        cons.append(f'table.CheckConstraint("{name}", "{q(sql)}");')
    cons = "\n                    ".join(cons)
    up.append(f'''            migrationBuilder.CreateTable(
                name: "{e["table"]}",
                columns: table => new
                {{
                    {cols}
                }},
                constraints: table =>
                {{
                    {cons}
                }});
''')
    for name, props, unique, filt, desc in e["idx"]:
        cols_ = ", ".join(f'"{col_of(e, p)}"' for p in props)
        extra = (", unique: true" if unique else "") + (f', filter: "{q(filt)}"' if filt else "") + \
                (", descending: new[] { " + ", ".join(cs_bool(d) for d in desc) + " }" if desc else "")
        colspec = f'column: "{col_of(e, props[0])}"' if len(props) == 1 else f"columns: new[] {{ {cols_} }}"
        up.append(f'''            migrationBuilder.CreateIndex(
                name: "{name}",
                table: "{e["table"]}",
                {colspec}{extra});
''')
down = "\n".join(f'            migrationBuilder.DropTable(name: "{e["table"]}");' for e in reversed(E))

migration = f'''using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace {NS};

/// <summary>
/// Initial CRM schema. A database created by the legacy SQL scripts (schema_migrations table) is baselined onto this
/// migration by DatabaseMigrator instead of running it. Generated from the schema spec in tools/gen_migration.py.
/// </summary>
public partial class InitialCrm : Migration
{{
    protected override void Up(MigrationBuilder migrationBuilder)
    {{
{chr(10).join(up)}    }}

    protected override void Down(MigrationBuilder migrationBuilder)
    {{
{down}
    }}
}}
'''

# ---- model (snapshot body) -----------------------------------------------------------------------------------------
def model_body(indent):
    L = []
    for e in E:
        L.append(f'modelBuilder.Entity("Crm.Api.Domain.{e["cls"]}", b =>\n{{')
        for p in e["props"]:
            clr = p.clr + ("?" if p.nullable and p.clr != "string" else "")
            line = f'    b.Property<{clr}>("{p.name}")'
            if p.identity: line += ".ValueGeneratedOnAdd()"
            if p.never: line += ".ValueGeneratedNever()"
            if not p.nullable and p.clr == "string": line += ".IsRequired()"
            if p.maxlen: line += f".HasMaxLength({p.maxlen})"
            if p.prec: line += f".HasPrecision({p.prec[0]}, {p.prec[1]})"
            line += f'.HasColumnType("{p.sql}").HasColumnName("{p.col}")'
            if p.default_sql: line += f'.HasDefaultValueSql("{p.default_sql}")'
            L.append(line + ";")
            if p.identity:
                L.append(f'    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<{p.clr}>("{p.name}"));')
        keys = ", ".join(f'"{k}"' for k in e["pk"])
        L.append(f'    b.HasKey({keys}).HasName("{e["pkname"]}");')
        for name, props, unique, filt, desc in e["idx"]:
            ps = ", ".join(f'"{p}"' for p in props)
            line = f'    b.HasIndex(new[] {{ {ps} }}, "{name}")'
            if unique: line += ".IsUnique()"
            if filt: line += f'.HasFilter("{q(filt)}")'
            if desc: line += ".IsDescending(" + ", ".join(cs_bool(d) for d in desc) + ")"
            L.append(line + ";")
        if e["checks"]:
            L.append(f'    b.ToTable("{e["table"]}", t =>\n    {{')
            for name, sql in e["checks"]: L.append(f'        t.HasCheckConstraint("{name}", "{q(sql)}");')
            L.append("    });")
        else:
            L.append(f'    b.ToTable("{e["table"]}");')
        L.append("});\n")
    for e in E:
        for prop, principal, name, pcol in e["fks"]:
            rel = "WithOne()" if e["cls"] == "CustomerIntegrationMapping" else "WithMany()"
            fk = f'.HasForeignKey<Crm.Api.Domain.{e["cls"]}>("{prop}")' if rel == "WithOne()" else f'.HasForeignKey("{prop}")'
            L.append(f'modelBuilder.Entity("Crm.Api.Domain.{e["cls"]}", b =>\n{{\n    b.HasOne("Crm.Api.Domain.{principal}", null).{rel}{fk}'
                     f'.HasConstraintName("{name}").OnDelete(DeleteBehavior.NoAction).IsRequired();\n}});\n')
    pad = " " * indent
    return "\n".join(pad + l if l else l for l in "\n".join(L).split("\n"))

HEAD = '''            modelBuilder
                .HasAnnotation("ProductVersion", "10.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

'''
usings = '''using System;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
'''
designer = f'''{usings}
namespace {NS};

[DbContext(typeof({CTX}))]
[Migration("{ID}")]
partial class InitialCrm
{{
    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {{
#pragma warning disable 612, 618
{HEAD}{model_body(12)}
#pragma warning restore 612, 618
    }}
}}
'''
snapshot = f'''{usings}
namespace {NS};

[DbContext(typeof({CTX}))]
partial class {CTX}ModelSnapshot : ModelSnapshot
{{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {{
#pragma warning disable 612, 618
{HEAD}{model_body(12)}
#pragma warning restore 612, 618
    }}
}}
'''
os.makedirs(out, exist_ok=True)
open(f"{out}/{ID}.cs", "w").write(migration)
open(f"{out}/{ID}.Designer.cs", "w").write(designer)
open(f"{out}/{CTX}ModelSnapshot.cs", "w").write(snapshot)
print("generated", len(E), "tables")
