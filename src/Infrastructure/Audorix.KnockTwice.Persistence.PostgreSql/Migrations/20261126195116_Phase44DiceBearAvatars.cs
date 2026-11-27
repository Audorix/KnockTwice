using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class Phase44DiceBearAvatars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "generated_avatar_seed",
                table: "user_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "generated_avatar_style",
                table: "user_profiles",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "generated_avatar_seed",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "generated_avatar_style",
                table: "user_profiles");
        }
    }
}
