using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Api.Infrastructure.Migrations;

/// <summary>
/// Case-insensitive unique email per tenant. EF cannot model a functional index (lower(email)), so it is raw SQL in a
/// migration of its own; the model snapshot is unaffected. CustomerService maps the resulting 23505 to a 409.
/// </summary>
[DbContext(typeof(FoundgineDbContext))]
[Migration("20261113000100_CustomerEmailUniqueIndex")]
public sealed class CustomerEmailUniqueIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("CREATE UNIQUE INDEX ux_customers_tenant_email ON customers (tenant_id, lower(email));");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP INDEX IF EXISTS ux_customers_tenant_email;");
}
