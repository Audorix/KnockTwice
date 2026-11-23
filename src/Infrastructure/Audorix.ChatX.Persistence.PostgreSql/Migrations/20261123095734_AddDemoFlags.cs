using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.ChatX.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_demo",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_demo",
                table: "organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_demo",
                table: "conversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Demo data loaded by earlier versions: accounts in the demo domain and what they created.
            migrationBuilder.Sql("UPDATE users SET is_demo = TRUE WHERE normalized_email LIKE '%@DEMO.CHATX.TEST';");
            migrationBuilder.Sql("UPDATE organizations SET is_demo = TRUE WHERE created_by_user_id IN (SELECT id FROM users WHERE is_demo);");
            migrationBuilder.Sql("UPDATE conversations SET is_demo = TRUE WHERE created_by_user_id IN (SELECT id FROM users WHERE is_demo);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_demo",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_demo",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "is_demo",
                table: "conversations");
        }
    }
}
