using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.ChatX.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    default_language = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    initialized_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    singleton = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation", x => x.id);
                    table.CheckConstraint("ck_installation_singleton", "singleton = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ux_installation_singleton",
                table: "installation",
                column: "singleton",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installation");
        }
    }
}
