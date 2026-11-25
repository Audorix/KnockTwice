using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "banner_content_type",
                table: "organizations",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "banner_key",
                table: "organizations",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "banner_version",
                table: "organizations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "photo_content_type",
                table: "organizations",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "photo_key",
                table: "organizations",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "photo_version",
                table: "organizations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "banner_content_type",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "banner_key",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "banner_version",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "photo_content_type",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "photo_key",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "photo_version",
                table: "organizations");
        }
    }
}
