using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class Phase35AccessControlAuditActorCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_access_group_members_users_added_by_user_id",
                table: "access_group_members");

            migrationBuilder.DropForeignKey(
                name: "fk_access_groups_users_created_by_user_id",
                table: "access_groups");

            migrationBuilder.DropForeignKey(
                name: "fk_access_role_groups_users_assigned_by_user_id",
                table: "access_role_groups");

            migrationBuilder.DropForeignKey(
                name: "fk_access_role_users_users_assigned_by_user_id",
                table: "access_role_users");

            migrationBuilder.DropForeignKey(
                name: "fk_access_roles_users_created_by_user_id",
                table: "access_roles");

            migrationBuilder.DropIndex(
                name: "ix_access_roles_created_by_user_id",
                table: "access_roles");

            migrationBuilder.DropIndex(
                name: "ix_access_role_users_assigned_by_user_id",
                table: "access_role_users");

            migrationBuilder.DropIndex(
                name: "ix_access_role_groups_assigned_by_user_id",
                table: "access_role_groups");

            migrationBuilder.DropIndex(
                name: "ix_access_groups_created_by_user_id",
                table: "access_groups");

            migrationBuilder.DropIndex(
                name: "ix_access_group_members_added_by_user_id",
                table: "access_group_members");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_access_roles_created_by_user_id",
                table: "access_roles",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_role_users_assigned_by_user_id",
                table: "access_role_users",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_role_groups_assigned_by_user_id",
                table: "access_role_groups",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_groups_created_by_user_id",
                table: "access_groups",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_group_members_added_by_user_id",
                table: "access_group_members",
                column: "added_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_access_group_members_users_added_by_user_id",
                table: "access_group_members",
                column: "added_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_access_groups_users_created_by_user_id",
                table: "access_groups",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_access_role_groups_users_assigned_by_user_id",
                table: "access_role_groups",
                column: "assigned_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_access_role_users_users_assigned_by_user_id",
                table: "access_role_users",
                column: "assigned_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_access_roles_users_created_by_user_id",
                table: "access_roles",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
