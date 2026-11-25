using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
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
                    entry_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    conversation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    sender_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    has_files = table.Column<bool>(type: "INTEGER", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_search", x => x.entry_id);
                });

            migrationBuilder.CreateTable(
                name: "search_queue",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    entry_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    conversation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    enqueued_at_ticks = table.Column<long>(type: "INTEGER", nullable: false)
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

            // Full-text matching (Phase 14): an FTS5 index over message_search.content, kept in step
            // by triggers (external content table). Kept outside the EF model: a future migration that
            // rebuilds message_search must recreate these triggers and run the FTS5 'rebuild' command.
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE message_search_fts USING fts5(
                    content, content='message_search', content_rowid='rowid', tokenize='unicode61 remove_diacritics 2');
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER message_search_ai AFTER INSERT ON message_search BEGIN
                    INSERT INTO message_search_fts(rowid, content) VALUES (new.rowid, new.content);
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER message_search_ad AFTER DELETE ON message_search BEGIN
                    INSERT INTO message_search_fts(message_search_fts, rowid, content) VALUES ('delete', old.rowid, old.content);
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER message_search_au AFTER UPDATE ON message_search BEGIN
                    INSERT INTO message_search_fts(message_search_fts, rowid, content) VALUES ('delete', old.rowid, old.content);
                    INSERT INTO message_search_fts(rowid, content) VALUES (new.rowid, new.content);
                END;
                """);

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
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS message_search_ai;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS message_search_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS message_search_au;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS message_search_fts;");
            migrationBuilder.DropTable(
                name: "message_search");

            migrationBuilder.DropTable(
                name: "search_queue");
        }
    }
}
