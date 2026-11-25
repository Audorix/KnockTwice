using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_key = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    reply_to_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_drafts", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_drafts_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_message_drafts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reminders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    remind_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminders", x => x.id);
                    table.ForeignKey(
                        name: "fk_reminders_conversation_events_conversation_id_entry_id",
                        columns: x => new { x.conversation_id, x.entry_id },
                        principalTable: "conversation_events",
                        principalColumns: new[] { "conversation_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reminders_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    reply_to_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    thread_root_id = table.Column<Guid>(type: "uuid", nullable: true),
                    also_in_conversation = table.Column<bool>(type: "boolean", nullable: false),
                    mention_user_ids = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    send_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    created_ticks = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_scheduled_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scheduled_messages_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_message_files",
                columns: table => new
                {
                    scheduled_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_message_files", x => new { x.scheduled_message_id, x.attachment_id });
                    table.ForeignKey(
                        name: "fk_scheduled_message_files_attachments_attachment_id",
                        column: x => x.attachment_id,
                        principalTable: "attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scheduled_message_files_scheduled_messages_scheduled_messag~",
                        column: x => x.scheduled_message_id,
                        principalTable: "scheduled_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_message_drafts_conversation_id",
                table: "message_drafts",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_drafts_updated_ticks",
                table: "message_drafts",
                column: "updated_ticks");

            migrationBuilder.CreateIndex(
                name: "ux_message_drafts",
                table: "message_drafts",
                columns: new[] { "user_id", "conversation_id", "thread_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reminders_conversation_id_entry_id",
                table: "reminders",
                columns: new[] { "conversation_id", "entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reminders_status_remind_at_ticks",
                table: "reminders",
                columns: new[] { "status", "remind_at_ticks" });

            migrationBuilder.CreateIndex(
                name: "ix_reminders_user_id",
                table: "reminders",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_message_files_attachment_id",
                table: "scheduled_message_files",
                column: "attachment_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_messages_conversation_id",
                table: "scheduled_messages",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_messages_status_send_at_ticks",
                table: "scheduled_messages",
                columns: new[] { "status", "send_at_ticks" });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_messages_user_id",
                table: "scheduled_messages",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_drafts");

            migrationBuilder.DropTable(
                name: "reminders");

            migrationBuilder.DropTable(
                name: "scheduled_message_files");

            migrationBuilder.DropTable(
                name: "scheduled_messages");
        }
    }
}
