using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundgine.Crm.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "crm_companies",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    industry = table.Column<string>(type: "text", nullable: true),
                    website = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crm_companies", x => x.id);
                    table.UniqueConstraint("uq_crm_companies_tenant_id", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "crm_contacts",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    last_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crm_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_crm_contacts_company",
                        columns: x => new { x.tenant_id, x.company_id },
                        principalSchema: "public",
                        principalTable: "crm_companies",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "crm_opportunities",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    stage = table.Column<string>(type: "text", nullable: false, defaultValue: "Qualification"),
                    expected_close_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crm_opportunities", x => x.id);
                    table.CheckConstraint("ck_crm_opportunities_amount_nonnegative", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_crm_opportunities_company",
                        columns: x => new { x.tenant_id, x.company_id },
                        principalSchema: "public",
                        principalTable: "crm_companies",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_crm_companies_tenant_name",
                schema: "public",
                table: "crm_companies",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_crm_contacts_tenant_email",
                schema: "public",
                table: "crm_contacts",
                columns: new[] { "tenant_id", "email" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_contacts_tenant_id_company_id",
                schema: "public",
                table: "crm_contacts",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_opportunities_tenant_id_company_id",
                schema: "public",
                table: "crm_opportunities",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_crm_opportunities_tenant_stage",
                schema: "public",
                table: "crm_opportunities",
                columns: new[] { "tenant_id", "stage" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crm_contacts",
                schema: "public");

            migrationBuilder.DropTable(
                name: "crm_opportunities",
                schema: "public");

            migrationBuilder.DropTable(
                name: "crm_companies",
                schema: "public");
        }
    }
}
