using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations;

/// <summary>Applied before the installation access-control migration; retained with its original ID for existing databases.</summary>
public partial class Phase35PrivateChatTeamPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>(
            name: "private_chats_require_shared_team",
            table: "organizations",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(
            name: "private_chats_require_shared_team",
            table: "organizations");
}
