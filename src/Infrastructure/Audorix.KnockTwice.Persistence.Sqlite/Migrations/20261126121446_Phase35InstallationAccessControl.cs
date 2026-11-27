using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Phase35InstallationAccessControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    normalized_name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "access_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    normalized_name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "access_group_members",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    added_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    added_by_user_id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_group_members", x => new { x.group_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_access_group_members_access_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "access_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_access_group_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "access_role_groups",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    group_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    assigned_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_role_groups", x => new { x.role_id, x.group_id });
                    table.ForeignKey(
                        name: "fk_access_role_groups_access_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "access_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_access_role_groups_access_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "access_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "access_role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    permission_key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_role_permissions", x => new { x.role_id, x.permission_key });
                    table.ForeignKey(
                        name: "fk_access_role_permissions_access_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "access_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "ix_access_group_members_user_id",
                table: "access_group_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_groups_normalized_name",
                table: "access_groups",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_role_groups_group_id",
                table: "access_role_groups",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_role_users_user_id",
                table: "access_role_users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_roles_normalized_name",
                table: "access_roles",
                column: "normalized_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_group_members");

            migrationBuilder.DropTable(
                name: "access_role_groups");

            migrationBuilder.DropTable(
                name: "access_role_permissions");

            migrationBuilder.DropTable(
                name: "access_role_users");

            migrationBuilder.DropTable(
                name: "access_groups");

            migrationBuilder.DropTable(
                name: "access_roles");

        }
    }
}
