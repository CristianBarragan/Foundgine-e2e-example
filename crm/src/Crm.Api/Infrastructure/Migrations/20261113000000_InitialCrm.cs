using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Crm.Api.Infrastructure.Migrations;

/// <summary>
/// Initial CRM schema. A database created by the legacy SQL scripts (schema_migrations table) is baselined onto this
/// migration by DatabaseMigrator instead of running it. Generated from the schema spec in tools/gen_migration.py.
/// </summary>
public partial class InitialCrm : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    customer_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("customers_pkey", x => x.customer_id);
                    table.CheckConstraint("customers_kind_check", "kind IN ('Business', 'Individual')");
                    table.CheckConstraint("customers_status_check", "status IN ('Active', 'Inactive')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_name",
                table: "customers",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    contact_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("contacts_pkey", x => x.contact_id);
                    table.ForeignKey(name: "contacts_customer_id_fkey", column: x => x.customer_id, principalTable: "customers", principalColumn: "customer_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_customer",
                table: "contacts",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateTable(
                name: "customer_integration_mappings",
                columns: table => new
                {
                    crm_customer_id = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    erp_customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("customer_integration_mappings_pkey", x => x.crm_customer_id);
                    table.ForeignKey(name: "customer_integration_mappings_crm_customer_id_fkey", column: x => x.crm_customer_id, principalTable: "customers", principalColumn: "customer_id");
                    table.CheckConstraint("customer_integration_mappings_status_check", "status IN ('Pending', 'Active', 'Failed')");
                });

            migrationBuilder.CreateIndex(
                name: "ux_mappings_tenant_erp",
                table: "customer_integration_mappings",
                columns: new[] { "tenant_id", "erp_customer_id" }, unique: true, filter: "erp_customer_id IS NOT NULL");

            migrationBuilder.CreateTable(
                name: "opportunities",
                columns: table => new
                {
                    opportunity_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: (int?)14m, scale: (int?)2m, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("opportunities_pkey", x => x.opportunity_id);
                    table.ForeignKey(name: "opportunities_customer_id_fkey", column: x => x.customer_id, principalTable: "customers", principalColumn: "customer_id");
                    table.CheckConstraint("opportunities_stage_check", "stage IN ('Prospecting','Qualification','Proposal','Negotiation','ClosedWon','ClosedLost')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_tenant_customer",
                table: "opportunities",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateTable(
                name: "quotes",
                columns: table => new
                {
                    quote_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    opportunity_id = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total = table.Column<decimal>(type: "numeric(14,2)", precision: (int?)14m, scale: (int?)2m, nullable: false),
                    submitted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    decided_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    erp_order_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    order_failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("quotes_pkey", x => x.quote_id);
                    table.ForeignKey(name: "quotes_opportunity_id_fkey", column: x => x.opportunity_id, principalTable: "opportunities", principalColumn: "opportunity_id");
                    table.ForeignKey(name: "quotes_customer_id_fkey", column: x => x.customer_id, principalTable: "customers", principalColumn: "customer_id");
                    table.CheckConstraint("quotes_status_check", "status IN ('Draft','PendingApproval','Approved','Rejected','Accepted','Ordered','OrderFailed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_quotes_tenant_customer",
                table: "quotes",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ux_quotes_tenant_erp_order",
                table: "quotes",
                columns: new[] { "tenant_id", "erp_order_id" }, unique: true, filter: "erp_order_id <> ''");

            migrationBuilder.CreateTable(
                name: "quote_lines",
                columns: table => new
                {
                    quote_line_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quote_id = table.Column<int>(type: "integer", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(14,2)", precision: (int?)14m, scale: (int?)2m, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("quote_lines_pkey", x => x.quote_line_id);
                    table.ForeignKey(name: "quote_lines_quote_id_fkey", column: x => x.quote_id, principalTable: "quotes", principalColumn: "quote_id");
                    table.CheckConstraint("quote_lines_quantity_check", "quantity > 0");
                    table.CheckConstraint("quote_lines_unit_price_check", "unit_price >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_quote_lines_quote",
                table: "quote_lines",
                columns: new[] { "tenant_id", "quote_id" });

            migrationBuilder.CreateTable(
                name: "return_requests",
                columns: table => new
                {
                    return_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quote_id = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    erp_return_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    decision_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    requested_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("return_requests_pkey", x => x.return_id);
                    table.ForeignKey(name: "return_requests_quote_id_fkey", column: x => x.quote_id, principalTable: "quotes", principalColumn: "quote_id");
                    table.ForeignKey(name: "return_requests_customer_id_fkey", column: x => x.customer_id, principalTable: "customers", principalColumn: "customer_id");
                    table.CheckConstraint("return_requests_status_check", "status IN ('Requested','Authorized','Rejected','Completed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_returns_quote",
                table: "return_requests",
                columns: new[] { "tenant_id", "quote_id" });

            migrationBuilder.CreateIndex(
                name: "ux_returns_tenant_erp",
                table: "return_requests",
                columns: new[] { "tenant_id", "erp_return_id" }, unique: true, filter: "erp_return_id <> ''");

            migrationBuilder.CreateTable(
                name: "return_request_lines",
                columns: table => new
                {
                    return_line_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    return_id = table.Column<int>(type: "integer", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("return_request_lines_pkey", x => x.return_line_id);
                    table.ForeignKey(name: "return_request_lines_return_id_fkey", column: x => x.return_id, principalTable: "return_requests", principalColumn: "return_id");
                    table.CheckConstraint("return_request_lines_quantity_check", "quantity > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_return_lines_return",
                table: "return_request_lines",
                columns: new[] { "tenant_id", "return_id" });

            migrationBuilder.CreateTable(
                name: "shipments",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    erp_shipment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quote_id = table.Column<int>(type: "integer", nullable: false),
                    erp_order_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shipments_pkey", x => new { x.tenant_id, x.erp_shipment_id });
                    table.ForeignKey(name: "shipments_quote_id_fkey", column: x => x.quote_id, principalTable: "quotes", principalColumn: "quote_id");
                    table.CheckConstraint("shipments_status_check", "status IN ('Packed','Shipped','InTransit','Delivered','Cancelled')");
                    table.CheckConstraint("shipments_version_check", "version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_quote",
                table: "shipments",
                columns: new[] { "tenant_id", "quote_id" });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    erp_invoice_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quote_id = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    erp_order_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    total = table.Column<decimal>(type: "numeric(14,2)", precision: (int?)14m, scale: (int?)2m, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTime?>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("invoices_pkey", x => new { x.tenant_id, x.erp_invoice_id });
                    table.ForeignKey(name: "invoices_quote_id_fkey", column: x => x.quote_id, principalTable: "quotes", principalColumn: "quote_id");
                    table.ForeignKey(name: "invoices_customer_id_fkey", column: x => x.customer_id, principalTable: "customers", principalColumn: "customer_id");
                    table.CheckConstraint("invoices_total_check", "total >= 0");
                    table.CheckConstraint("invoices_status_check", "status IN ('Issued','PartiallyPaid','Paid','Overdue','Void')");
                    table.CheckConstraint("invoices_version_check", "version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_quote",
                table: "invoices",
                columns: new[] { "tenant_id", "quote_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_customer",
                table: "invoices",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    erp_payment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    erp_invoice_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quote_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: (int?)14m, scale: (int?)2m, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("payments_pkey", x => new { x.tenant_id, x.erp_payment_id });
                    table.ForeignKey(name: "payments_quote_id_fkey", column: x => x.quote_id, principalTable: "quotes", principalColumn: "quote_id");
                    table.CheckConstraint("payments_amount_check", "amount >= 0");
                    table.CheckConstraint("payments_status_check", "status IN ('Received','Reconciled','Reversed')");
                    table.CheckConstraint("payments_version_check", "version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_quote",
                table: "payments",
                columns: new[] { "tenant_id", "quote_id" });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    aggregate_id = table.Column<int>(type: "integer", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTime?>(type: "timestamp with time zone", nullable: true),
                    publish_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("outbox_messages_pkey", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_unpublished",
                table: "outbox_messages",
                column: "next_attempt_at", filter: "published_at IS NULL");

            migrationBuilder.CreateTable(
                name: "admin_audit",
                columns: table => new
                {
                    audit_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    detail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("admin_audit_pkey", x => x.audit_id);
                    table.CheckConstraint("admin_audit_outcome_check", "outcome IN ('Succeeded','Rejected','Failed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_tenant_time",
                table: "admin_audit",
                columns: new[] { "tenant_id", "occurred_at" }, descending: new[] { false, true });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
            migrationBuilder.DropTable(name: "admin_audit");
            migrationBuilder.DropTable(name: "outbox_messages");
            migrationBuilder.DropTable(name: "payments");
            migrationBuilder.DropTable(name: "invoices");
            migrationBuilder.DropTable(name: "shipments");
            migrationBuilder.DropTable(name: "return_request_lines");
            migrationBuilder.DropTable(name: "return_requests");
            migrationBuilder.DropTable(name: "quote_lines");
            migrationBuilder.DropTable(name: "quotes");
            migrationBuilder.DropTable(name: "opportunities");
            migrationBuilder.DropTable(name: "customer_integration_mappings");
            migrationBuilder.DropTable(name: "contacts");
            migrationBuilder.DropTable(name: "customers");
    }
}
