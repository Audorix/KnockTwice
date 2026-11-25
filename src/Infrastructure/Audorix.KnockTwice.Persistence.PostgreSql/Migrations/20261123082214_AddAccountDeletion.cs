using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at_utc",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at_utc",
                table: "user_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "created_at_ticks",
                table: "conversation_events",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Existing history entries: creation time as .NET ticks (100 ns since 0001-01-01 UTC).
            migrationBuilder.Sql(
                "UPDATE conversation_events SET created_at_ticks = (EXTRACT(EPOCH FROM created_at_utc) * 10000000)::bigint + 621355968000000000;");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_events_created_at_ticks",
                table: "conversation_events",
                column: "created_at_ticks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_conversation_events_created_at_ticks",
                table: "conversation_events");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "users");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "created_at_ticks",
                table: "conversation_events");
        }
    }
}
