using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.PostgreSql.Migrations
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
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sso_client_id",
                table: "organizations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sso_issuer",
                table: "organizations",
                type: "character varying(512)",
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
