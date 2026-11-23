using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.ChatX.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "desktop_notifications",
                table: "user_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "notification_previews",
                table: "user_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "theme",
                table: "user_profiles",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "system");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_profiles_theme",
                table: "user_profiles",
                sql: "theme IN ('system', 'light', 'dark')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_profiles_theme",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "desktop_notifications",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "notification_previews",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "theme",
                table: "user_profiles");
        }
    }
}
