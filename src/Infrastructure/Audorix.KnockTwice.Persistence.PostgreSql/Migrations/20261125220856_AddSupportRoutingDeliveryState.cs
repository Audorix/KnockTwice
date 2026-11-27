using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportRoutingDeliveryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_support_routing_outbox_delivery",
                table: "support_routing_outbox");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at_utc",
                table: "support_routing_outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dead_lettered_at_utc",
                table: "support_routing_outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lease_token",
                table: "support_routing_outbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_support_routing_outbox_delivery",
                table: "support_routing_outbox",
                columns: new[] { "completed_at_utc", "dead_lettered_at_utc", "published_at_utc", "available_at_utc", "lease_expires_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_support_routing_outbox_delivery",
                table: "support_routing_outbox");

            migrationBuilder.DropColumn(
                name: "completed_at_utc",
                table: "support_routing_outbox");

            migrationBuilder.DropColumn(
                name: "dead_lettered_at_utc",
                table: "support_routing_outbox");

            migrationBuilder.DropColumn(
                name: "lease_token",
                table: "support_routing_outbox");

            migrationBuilder.CreateIndex(
                name: "ix_support_routing_outbox_delivery",
                table: "support_routing_outbox",
                columns: new[] { "published_at_utc", "available_at_utc", "lease_expires_at_utc" });
        }
    }
}
