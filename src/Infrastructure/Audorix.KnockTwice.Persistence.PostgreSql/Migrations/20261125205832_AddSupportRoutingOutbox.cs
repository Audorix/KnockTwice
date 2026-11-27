using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportRoutingOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "support_routing_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    enqueued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_routing_outbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_support_routing_outbox_conversation",
                table: "support_routing_outbox",
                columns: new[] { "organization_id", "conversation_id", "enqueued_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_support_routing_outbox_delivery",
                table: "support_routing_outbox",
                columns: new[] { "published_at_utc", "available_at_utc", "lease_expires_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_routing_outbox");
        }
    }
}
