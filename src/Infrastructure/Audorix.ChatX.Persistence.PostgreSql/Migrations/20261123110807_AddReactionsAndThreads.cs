using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.ChatX.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddReactionsAndThreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "shown_in_conversation",
                table: "conversation_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "thread_last_reply_at_utc",
                table: "conversation_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "thread_reply_count",
                table: "conversation_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "thread_root_id",
                table: "conversation_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "message_reactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emoji = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_reactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_reactions_conversation_events_conversation_id_entry~",
                        columns: x => new { x.conversation_id, x.entry_id },
                        principalTable: "conversation_events",
                        principalColumns: new[] { "conversation_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_message_reactions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thread_follows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    root_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    following = table.Column<bool>(type: "boolean", nullable: false),
                    last_read_sequence = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thread_follows", x => x.id);
                    table.ForeignKey(
                        name: "fk_thread_follows_conversation_events_conversation_id_root_id",
                        columns: x => new { x.conversation_id, x.root_id },
                        principalTable: "conversation_events",
                        principalColumns: new[] { "conversation_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_thread_follows_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_events_thread",
                table: "conversation_events",
                columns: new[] { "thread_root_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_message_reactions_conversation_id",
                table: "message_reactions",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_reactions_conversation_id_entry_id",
                table: "message_reactions",
                columns: new[] { "conversation_id", "entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_message_reactions_user_id",
                table: "message_reactions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_message_reactions",
                table: "message_reactions",
                columns: new[] { "entry_id", "user_id", "emoji" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_thread_follows_conversation_id_root_id",
                table: "thread_follows",
                columns: new[] { "conversation_id", "root_id" });

            migrationBuilder.CreateIndex(
                name: "ix_thread_follows_user_id",
                table: "thread_follows",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_thread_follows",
                table: "thread_follows",
                columns: new[] { "root_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_reactions");

            migrationBuilder.DropTable(
                name: "thread_follows");

            migrationBuilder.DropIndex(
                name: "ix_conversation_events_thread",
                table: "conversation_events");

            migrationBuilder.DropColumn(
                name: "shown_in_conversation",
                table: "conversation_events");

            migrationBuilder.DropColumn(
                name: "thread_last_reply_at_utc",
                table: "conversation_events");

            migrationBuilder.DropColumn(
                name: "thread_reply_count",
                table: "conversation_events");

            migrationBuilder.DropColumn(
                name: "thread_root_id",
                table: "conversation_events");
        }
    }
}
