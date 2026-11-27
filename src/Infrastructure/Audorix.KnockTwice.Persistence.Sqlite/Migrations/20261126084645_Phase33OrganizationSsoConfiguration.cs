using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Phase33OrganizationSsoConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "protected_sso_client_secret",
                table: "organizations",
                type: "TEXT",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sso_client_id",
                table: "organizations",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sso_issuer",
                table: "organizations",
                type: "TEXT",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "protected_sso_client_secret",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "sso_client_id",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "sso_issuer",
                table: "organizations");
        }
    }
}
