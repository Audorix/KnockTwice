using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_conversation_events_message",
                table: "conversation_events");

            migrationBuilder.AddColumn<Guid>(
                name: "forwarded_from_user_id",
                table: "conversation_events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "message_pins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    conversation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    entry_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    pinned_by_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    pinned_ticks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_pins", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_pins_conversation_events_conversation_id_entry_id",
                        columns: x => new { x.conversation_id, x.entry_id },
                        principalTable: "conversation_events",
                        principalColumns: new[] { "conversation_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_message_pins_users_pinned_by_user_id",
                        column: x => x.pinned_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "saved_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    conversation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    entry_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    saved_ticks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_saved_messages_conversation_events_conversation_id_entry_id",
                        columns: x => new { x.conversation_id, x.entry_id },
                        principalTable: "conversation_events",
                        principalColumns: new[] { "conversation_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_saved_messages_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversation_events_message",
                table: "conversation_events",
                sql: "(kind = 'Message' AND client_message_id IS NOT NULL AND (body IS NOT NULL OR deleted_at_utc IS NOT NULL)) OR (kind <> 'Message' AND body IS NULL AND client_message_id IS NULL AND (reply_to_entry_id IS NULL OR kind = 'Pinned'))");

            migrationBuilder.CreateIndex(
                name: "ix_message_pins_conversation_id_entry_id",
                table: "message_pins",
                columns: new[] { "conversation_id", "entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_message_pins_conversation_id_pinned_ticks",
                table: "message_pins",
                columns: new[] { "conversation_id", "pinned_ticks" });

            migrationBuilder.CreateIndex(
                name: "ix_message_pins_pinned_by_user_id",
                table: "message_pins",
                column: "pinned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_message_pins",
                table: "message_pins",
                column: "entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saved_messages_conversation_id_entry_id",
                table: "saved_messages",
                columns: new[] { "conversation_id", "entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_saved_messages_user_id_saved_ticks",
                table: "saved_messages",
                columns: new[] { "user_id", "saved_ticks" });

            migrationBuilder.CreateIndex(
                name: "ux_saved_messages",
                table: "saved_messages",
                columns: new[] { "user_id", "entry_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_pins");

            migrationBuilder.DropTable(
                name: "saved_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversation_events_message",
                table: "conversation_events");

            migrationBuilder.DropColumn(
                name: "forwarded_from_user_id",
                table: "conversation_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversation_events_message",
                table: "conversation_events",
                sql: "(kind = 'Message' AND client_message_id IS NOT NULL AND (body IS NOT NULL OR deleted_at_utc IS NOT NULL)) OR (kind <> 'Message' AND body IS NULL AND client_message_id IS NULL AND reply_to_entry_id IS NULL)");
        }
    }
}
