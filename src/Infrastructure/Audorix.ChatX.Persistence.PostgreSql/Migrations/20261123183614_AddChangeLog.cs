using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Audorix.ChatX.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "change_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_change_log_conversation_id_id",
                table: "change_log",
                columns: new[] { "conversation_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_change_log_recorded_ticks",
                table: "change_log",
                column: "recorded_ticks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "change_log");
        }
    }
}
