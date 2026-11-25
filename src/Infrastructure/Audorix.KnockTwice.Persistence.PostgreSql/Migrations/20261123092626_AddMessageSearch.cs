using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Audorix.KnockTwice.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_search",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    has_files = table.Column<bool>(type: "boolean", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_search", x => x.entry_id);
                });

            migrationBuilder.CreateTable(
                name: "search_queue",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enqueued_at_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_queue", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_message_search_conversation_id_sequence",
                table: "message_search",
                columns: new[] { "conversation_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_message_search_created_at_ticks",
                table: "message_search",
                column: "created_at_ticks");

            // Full-text matching (Phase 14): the text is normalized by the application (lower case,
            // no accents), so the language-neutral 'simple' configuration is enough; prefix queries
            // use the GIN index. Kept outside the EF model.
            migrationBuilder.Sql(
                "ALTER TABLE message_search ADD COLUMN document tsvector GENERATED ALWAYS AS (to_tsvector('simple', content)) STORED;");
            migrationBuilder.Sql("CREATE INDEX ix_message_search_document ON message_search USING GIN (document);");

            // Index every existing message: the indexer works through the queue after the upgrade.
            migrationBuilder.Sql("""
                INSERT INTO search_queue (entry_id, conversation_id, enqueued_at_ticks)
                SELECT id, conversation_id, created_at_ticks FROM conversation_events
                WHERE kind = 'Message' AND deleted_at_utc IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_search");

            migrationBuilder.DropTable(
                name: "search_queue");
        }
    }
}
