using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Phase37GroupBasedAllowDenyAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "effect",
                table: "access_role_permissions",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "Allow");

            migrationBuilder.Sql("""
                CREATE TEMP TABLE __migrated_access_groups (user_id TEXT NOT NULL PRIMARY KEY, group_id TEXT NOT NULL);
                INSERT INTO __migrated_access_groups (user_id, group_id)
                SELECT user_id,
                    lower(substr(hex(randomblob(16)), 1, 8) || '-' || substr(hex(randomblob(16)), 9, 4) || '-' ||
                          substr(hex(randomblob(16)), 13, 4) || '-' || substr(hex(randomblob(16)), 17, 4) || '-' ||
                          substr(hex(randomblob(16)), 21, 12))
                FROM (SELECT DISTINCT user_id FROM access_role_users);
                INSERT INTO access_groups (id, name, normalized_name, description, created_by_user_id, created_at_utc)
                SELECT migrated.group_id, '__Migrated direct role user ' || migrated.user_id,
                    upper('__Migrated direct role user ' || migrated.user_id), 'Preserved from a prior direct role assignment.',
                    (SELECT assignment.assigned_by_user_id FROM access_role_users assignment WHERE assignment.user_id = migrated.user_id ORDER BY assignment.assigned_at_utc LIMIT 1),
                    (SELECT assignment.assigned_at_utc FROM access_role_users assignment WHERE assignment.user_id = migrated.user_id ORDER BY assignment.assigned_at_utc LIMIT 1)
                FROM __migrated_access_groups migrated;
                INSERT INTO access_group_members (group_id, user_id, added_at_utc, added_by_user_id)
                SELECT migrated.group_id, assignment.user_id, assignment.assigned_at_utc, assignment.assigned_by_user_id
                FROM access_role_users assignment JOIN __migrated_access_groups migrated ON migrated.user_id = assignment.user_id;
                INSERT INTO access_role_groups (role_id, group_id, assigned_at_utc, assigned_by_user_id)
                SELECT assignment.role_id, migrated.group_id, assignment.assigned_at_utc, assignment.assigned_by_user_id
                FROM access_role_users assignment JOIN __migrated_access_groups migrated ON migrated.user_id = assignment.user_id;
                DROP TABLE __migrated_access_groups;
                """);
            migrationBuilder.DropTable(name: "access_role_users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "effect",
                table: "access_role_permissions");

            migrationBuilder.CreateTable(
                name: "access_role_users",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    assigned_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_role_users", x => new { x.role_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_access_role_users_access_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "access_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_access_role_users_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_role_users_user_id",
                table: "access_role_users",
                column: "user_id");

            migrationBuilder.Sql("""
                INSERT INTO access_role_users (role_id, user_id, assigned_at_utc, assigned_by_user_id)
                SELECT role_assignment.role_id, member.user_id, role_assignment.assigned_at_utc, role_assignment.assigned_by_user_id
                FROM access_role_groups role_assignment
                JOIN access_groups migrated ON migrated.id = role_assignment.group_id
                JOIN access_group_members member ON member.group_id = migrated.id
                WHERE substr(migrated.name, 1, length('__Migrated direct role user ')) = '__Migrated direct role user ';
                DELETE FROM access_groups
                WHERE substr(name, 1, length('__Migrated direct role user ')) = '__Migrated direct role user ';
                """);
        }
    }
}
