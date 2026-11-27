using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class Phase35ScopedInstallationAccessControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_groups_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "access_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_roles_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "access_group_members",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                        name: "fk_access_group_members_users_added_by_user_id",
                        column: x => x.added_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    table.ForeignKey(
                        name: "fk_access_role_groups_users_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "access_role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
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
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                        name: "fk_access_role_users_users_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_role_users_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_group_members_added_by_user_id",
                table: "access_group_members",
                column: "added_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_group_members_user_id",
                table: "access_group_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_groups_created_by_user_id",
                table: "access_groups",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_groups_normalized_name",
                table: "access_groups",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_role_groups_assigned_by_user_id",
                table: "access_role_groups",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_role_groups_group_id",
                table: "access_role_groups",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_role_users_assigned_by_user_id",
                table: "access_role_users",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_role_users_user_id",
                table: "access_role_users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_roles_created_by_user_id",
                table: "access_roles",
                column: "created_by_user_id");

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
