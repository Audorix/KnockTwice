using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.ChatX.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilePhotosAndLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "photo_content_type",
                table: "user_profiles",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "photo_key",
                table: "user_profiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "photo_version",
                table: "user_profiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "profile_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    platform = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    url = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    visibility = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profile_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_profile_links_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_profile_links_user_id_position",
                table: "profile_links",
                columns: new[] { "user_id", "position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profile_links");

            migrationBuilder.DropColumn(
                name: "photo_content_type",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "photo_key",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "photo_version",
                table: "user_profiles");
        }
    }
}
