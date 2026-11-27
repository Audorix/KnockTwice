using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportRoutingQueueTicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_support_routing_outbox_delivery",
                table: "support_routing_outbox");

            migrationBuilder.AddColumn<long>(
                name: "available_at_ticks",
                table: "support_routing_outbox",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "enqueued_at_ticks",
                table: "support_routing_outbox",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "lease_expires_at_ticks",
                table: "support_routing_outbox",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_support_routing_outbox_delivery",
                table: "support_routing_outbox",
                columns: new[] { "completed_at_utc", "dead_lettered_at_utc", "published_at_utc", "available_at_ticks", "lease_expires_at_ticks" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_support_routing_outbox_delivery",
                table: "support_routing_outbox");

            migrationBuilder.DropColumn(
                name: "available_at_ticks",
                table: "support_routing_outbox");

            migrationBuilder.DropColumn(
                name: "enqueued_at_ticks",
                table: "support_routing_outbox");

            migrationBuilder.DropColumn(
                name: "lease_expires_at_ticks",
                table: "support_routing_outbox");

            migrationBuilder.CreateIndex(
                name: "ix_support_routing_outbox_delivery",
                table: "support_routing_outbox",
                columns: new[] { "completed_at_utc", "dead_lettered_at_utc", "published_at_utc", "available_at_utc", "lease_expires_at_utc" });
        }
    }
}
