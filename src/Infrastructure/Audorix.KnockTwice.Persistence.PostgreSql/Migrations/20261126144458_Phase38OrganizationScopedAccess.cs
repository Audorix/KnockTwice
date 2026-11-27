using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class Phase38OrganizationScopedAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_access_roles_normalized_name",
                table: "access_roles");

            migrationBuilder.DropIndex(
                name: "ix_access_groups_normalized_name",
                table: "access_groups");

            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "access_roles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scope",
                table: "access_roles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "installation");

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "access_roles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "access_groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scope",
                table: "access_groups",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "installation");

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "access_groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_roles_organization_id",
                table: "access_roles",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_roles_scope_organization_id_workspace_id_normalized_~",
                table: "access_roles",
                columns: new[] { "scope", "organization_id", "workspace_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_roles_workspace_id",
                table: "access_roles",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_groups_organization_id",
                table: "access_groups",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_groups_scope_organization_id_workspace_id_normalized~",
                table: "access_groups",
                columns: new[] { "scope", "organization_id", "workspace_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_groups_workspace_id",
                table: "access_groups",
                column: "workspace_id");

            migrationBuilder.AddForeignKey(
                name: "fk_access_groups_organizations_organization_id",
                table: "access_groups",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_access_groups_workspaces_workspace_id",
                table: "access_groups",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_access_roles_organizations_organization_id",
                table: "access_roles",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_access_roles_workspaces_workspace_id",
                table: "access_roles",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_access_groups_organizations_organization_id",
                table: "access_groups");

            migrationBuilder.DropForeignKey(
                name: "fk_access_groups_workspaces_workspace_id",
                table: "access_groups");

            migrationBuilder.DropForeignKey(
                name: "fk_access_roles_organizations_organization_id",
                table: "access_roles");

            migrationBuilder.DropForeignKey(
                name: "fk_access_roles_workspaces_workspace_id",
                table: "access_roles");

            migrationBuilder.DropIndex(
                name: "ix_access_roles_organization_id",
                table: "access_roles");

            migrationBuilder.DropIndex(
                name: "ix_access_roles_scope_organization_id_workspace_id_normalized_~",
                table: "access_roles");

            migrationBuilder.DropIndex(
                name: "ix_access_roles_workspace_id",
                table: "access_roles");

            migrationBuilder.DropIndex(
                name: "ix_access_groups_organization_id",
                table: "access_groups");

            migrationBuilder.DropIndex(
                name: "ix_access_groups_scope_organization_id_workspace_id_normalized~",
                table: "access_groups");

            migrationBuilder.DropIndex(
                name: "ix_access_groups_workspace_id",
                table: "access_groups");

            migrationBuilder.DropColumn(
                name: "organization_id",
                table: "access_roles");

            migrationBuilder.DropColumn(
                name: "scope",
                table: "access_roles");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "access_roles");

            migrationBuilder.DropColumn(
                name: "organization_id",
                table: "access_groups");

            migrationBuilder.DropColumn(
                name: "scope",
                table: "access_groups");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "access_groups");

            migrationBuilder.CreateIndex(
                name: "ix_access_roles_normalized_name",
                table: "access_roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_groups_normalized_name",
                table: "access_groups",
                column: "normalized_name",
                unique: true);
        }
    }
}
