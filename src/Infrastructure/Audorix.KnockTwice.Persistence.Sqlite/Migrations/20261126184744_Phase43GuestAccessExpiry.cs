using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Phase43GuestAccessExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "access_expires_at_utc",
                table: "organization_guests",
                type: "TEXT",
                nullable: false,
                defaultValue: DateTimeOffset.UtcNow.AddDays(90));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "access_expires_at_utc",
                table: "organization_guests");
        }
    }
}
