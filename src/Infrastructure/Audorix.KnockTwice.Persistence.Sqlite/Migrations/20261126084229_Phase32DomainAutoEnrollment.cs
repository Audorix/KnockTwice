using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Phase32DomainAutoEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "automatic_enrollment_enabled",
                table: "organizations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "automatic_enrollment_enabled",
                table: "organizations");

        }
    }
}
