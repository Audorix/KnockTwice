using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.ChatX.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notification_level",
                table: "conversation_participants",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "All");

            // Owner decision (Phase 17): an existing mute becomes "only mentions".
            migrationBuilder.Sql("UPDATE conversation_participants SET notification_level = 'Mentions' WHERE muted = 1;");

            migrationBuilder.DropColumn(
                name: "muted",
                table: "conversation_participants");

            migrationBuilder.CreateTable(
                name: "message_mentions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    conversation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    entry_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    sent_ticks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_mentions", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_mentions_conversation_events_conversation_id_entry_id",
                        columns: x => new { x.conversation_id, x.entry_id },
                        principalTable: "conversation_events",
                        principalColumns: new[] { "conversation_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_message_mentions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_message_mentions_conversation_id_entry_id",
                table: "message_mentions",
                columns: new[] { "conversation_id", "entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_message_mentions_user",
                table: "message_mentions",
                columns: new[] { "user_id", "sent_ticks" });

            migrationBuilder.CreateIndex(
                name: "ux_message_mentions",
                table: "message_mentions",
                columns: new[] { "entry_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_mentions");

            migrationBuilder.DropColumn(
                name: "notification_level",
                table: "conversation_participants");

            migrationBuilder.AddColumn<bool>(
                name: "muted",
                table: "conversation_participants",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
