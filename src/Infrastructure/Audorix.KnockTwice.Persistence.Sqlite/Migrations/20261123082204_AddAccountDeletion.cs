using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
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
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at_utc",
                table: "user_profiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "created_at_ticks",
                table: "conversation_events",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // Existing history entries: creation time as .NET ticks (100 ns since 0001-01-01 UTC).
            // EF stores DateTimeOffset as 'yyyy-MM-dd HH:mm:ss[.fffffff]+00:00' (always UTC here).
            migrationBuilder.Sql("""
                UPDATE conversation_events SET created_at_ticks =
                    (CAST(strftime('%s', substr(created_at_utc, 1, 19)) AS INTEGER) + 62135596800) * 10000000
                    + CASE WHEN substr(created_at_utc, 20, 1) = '.'
                           THEN CAST(substr(substr(created_at_utc, 21, length(created_at_utc) - 26) || '0000000', 1, 7) AS INTEGER)
                           ELSE 0 END;
                """);

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
