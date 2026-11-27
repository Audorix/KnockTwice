using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class Phase49SavedMessageTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_translations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original_text = table.Column<string>(type: "text", nullable: false),
                    translated_text = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_translations", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_translations_conversation_events_conversation_id_me~",
                        columns: x => new { x.conversation_id, x.message_id },
                        principalTable: "conversation_events",
                        principalColumns: new[] { "conversation_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_message_translations_conversation_id_message_id",
                table: "message_translations",
                columns: new[] { "conversation_id", "message_id" });

            migrationBuilder.CreateIndex(
                name: "ux_message_translations_message_language_source",
                table: "message_translations",
                columns: new[] { "message_id", "target_language", "source_sha256" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_translations");
        }
    }
}
