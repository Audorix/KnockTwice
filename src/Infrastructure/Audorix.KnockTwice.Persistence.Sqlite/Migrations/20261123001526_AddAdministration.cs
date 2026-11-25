using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "disabled_at_utc",
                table: "users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurred_at_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    target_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    target_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ip_address = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    details = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "installation_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_settings", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_action_occurred_at_ticks",
                table: "audit_entries",
                columns: new[] { "action", "occurred_at_ticks" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_user_id_occurred_at_ticks",
                table: "audit_entries",
                columns: new[] { "actor_user_id", "occurred_at_ticks" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at_ticks",
                table: "audit_entries",
                column: "occurred_at_ticks");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_target_id",
                table: "audit_entries",
                column: "target_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "installation_settings");

            migrationBuilder.DropColumn(
                name: "disabled_at_utc",
                table: "users");
        }
    }
}
