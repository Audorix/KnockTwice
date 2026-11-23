using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.ChatX.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rejection_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    message_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    ready_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content_removed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                    table.CheckConstraint("ck_attachments_linked_ready", "message_entry_id IS NULL OR status IN ('Ready', 'Deleted')");
                    table.CheckConstraint("ck_attachments_size", "size >= 0");
                    table.ForeignKey(
                        name: "fk_attachments_conversation_events_conversation_id_message_ent~",
                        columns: x => new { x.conversation_id, x.message_entry_id },
                        principalTable: "conversation_events",
                        principalColumns: new[] { "conversation_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attachments_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attachments_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_conversation_id_message_entry_id",
                table: "attachments",
                columns: new[] { "conversation_id", "message_entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_message_entry_id",
                table: "attachments",
                column: "message_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_owner_user_id_status",
                table: "attachments",
                columns: new[] { "owner_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_status_created_at_ticks",
                table: "attachments",
                columns: new[] { "status", "created_at_ticks" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_storage_key",
                table: "attachments",
                column: "storage_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachments");
        }
    }
}
