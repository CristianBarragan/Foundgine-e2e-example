using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Api.Infrastructure.Migrations;

/// <summary>Records which Keycloak subject caused an outbox message, so Temporal workflows can carry the initiator.</summary>
[DbContext(typeof(FoundgineDbContext))]
[Migration("20261113000200_OutboxInitiator")]
public sealed class OutboxInitiator : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "initiator", table: "outbox_messages", type: "character varying(128)",
            maxLength: 128, nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "initiator", table: "outbox_messages");
}
