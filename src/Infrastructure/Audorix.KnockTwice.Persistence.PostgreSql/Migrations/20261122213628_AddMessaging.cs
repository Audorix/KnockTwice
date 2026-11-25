using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_delivered_sequence",
                table: "conversation_participants",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Existing participants have received everything they have read (keeps ck_conversation_participants_delivered valid).
            migrationBuilder.Sql("UPDATE conversation_participants SET last_delivered_sequence = last_read_sequence;");

            migrationBuilder.AddColumn<string>(
                name: "body",
                table: "conversation_events",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "client_message_id",
                table: "conversation_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at_utc",
                table: "conversation_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_user_id",
                table: "conversation_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "edited_at_utc",
                table: "conversation_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reply_to_entry_id",
                table: "conversation_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "conversation_events",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_conversation_events_conversation_id_id",
                table: "conversation_events",
                columns: new[] { "conversation_id", "id" });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_ticks = table.Column<long>(type: "bigint", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_outbox_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversation_participants_delivered",
                table: "conversation_participants",
                sql: "last_delivered_sequence >= last_read_sequence");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_events_conversation_id_reply_to_entry_id",
                table: "conversation_events",
                columns: new[] { "conversation_id", "reply_to_entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_events_deleted_by_user_id",
                table: "conversation_events",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_conversation_events_client_message",
                table: "conversation_events",
                columns: new[] { "conversation_id", "actor_user_id", "client_message_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversation_events_message",
                table: "conversation_events",
                sql: "(kind = 'Message' AND client_message_id IS NOT NULL AND (body IS NOT NULL OR deleted_at_utc IS NOT NULL)) OR (kind <> 'Message' AND body IS NULL AND client_message_id IS NULL AND reply_to_entry_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversation_events_version",
                table: "conversation_events",
                sql: "version >= 1");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_conversation_id",
                table: "outbox_messages",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_next_attempt_ticks",
                table: "outbox_messages",
                column: "next_attempt_ticks");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_events_conversation_events_conversation_id_rep~",
                table: "conversation_events",
                columns: new[] { "conversation_id", "reply_to_entry_id" },
                principalTable: "conversation_events",
                principalColumns: new[] { "conversation_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_events_users_deleted_by_user_id",
                table: "conversation_events",
                column: "deleted_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_conversation_events_conversation_events_conversation_id_rep~",
                table: "conversation_events");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_events_users_deleted_by_user_id",
                table: "conversation_events");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversation_participants_delivered",
                table: "conversation_participants");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_conversation_events_conversation_id_id",
                table: "conversation_events");

            migrationBuilder.DropIndex(
                name: "ix_conversation_events_conversation_id_reply_to_entry_id",
                table: "conversation_events");

            migrationBuilder.DropIndex(
                name: "ix_conversation_events_deleted_by_user_id",
                table: "conversation_events");

            migrationBuilder.DropIndex(
                name: "ux_conversation_events_client_message",
                table: "conversation_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversation_events_message",
                table: "conversation_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversation_events_version",
                table: "conversation_events");

            migrationBuilder.DropColumn(
                name: "last_delivered_sequence",
                table: "conversation_participants");

            migrationBuilder.DropColumn(
                name: "body",
                table: "conversation_events");

            migrationBuilder.DropColumn(
                name: "client_message_id",
                table: "conversation_events");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "conversation_events");

            migrationBuilder.DropColumn(
                name: "deleted_by_user_id",
                table: "conversation_events");

            migrationBuilder.DropColumn(
                name: "edited_at_utc",
                table: "conversation_events");

            migrationBuilder.DropColumn(
                name: "reply_to_entry_id",
                table: "conversation_events");

            migrationBuilder.DropColumn(
                name: "version",
                table: "conversation_events");
        }
    }
}
