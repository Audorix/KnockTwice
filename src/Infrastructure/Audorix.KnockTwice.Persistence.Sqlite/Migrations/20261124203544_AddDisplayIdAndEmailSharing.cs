using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddDisplayIdAndEmailSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "handle",
                table: "user_profiles",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "share_email_with_contacts",
                table: "user_profiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_user_profiles_handle",
                table: "user_profiles",
                column: "handle",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_profiles_handle",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "handle",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "share_email_with_contacts",
                table: "user_profiles");
        }
    }
}
