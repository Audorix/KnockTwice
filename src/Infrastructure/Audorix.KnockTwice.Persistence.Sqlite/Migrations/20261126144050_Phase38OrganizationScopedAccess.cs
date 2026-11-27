using Audorix.KnockTwice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations;

[DbContext(typeof(ChatXDbContext))]
[Migration("20261126144050_Phase38OrganizationScopedAccess")]
public sealed class Phase38OrganizationScopedAccess : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("ix_access_roles_normalized_name", "access_roles");
        migrationBuilder.DropIndex("ix_access_groups_normalized_name", "access_groups");

        migrationBuilder.AddColumn<Guid>("organization_id", "access_roles", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("scope", "access_roles", type: "TEXT", maxLength: 20, nullable: false, defaultValue: "installation");
        migrationBuilder.AddColumn<Guid>("workspace_id", "access_roles", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<Guid>("organization_id", "access_groups", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("scope", "access_groups", type: "TEXT", maxLength: 20, nullable: false, defaultValue: "installation");
        migrationBuilder.AddColumn<Guid>("workspace_id", "access_groups", type: "TEXT", nullable: true);

        migrationBuilder.CreateIndex("ix_access_roles_organization_id", "access_roles", "organization_id");
        migrationBuilder.CreateIndex("ix_access_roles_scope_organization_id_workspace_id_normalized_name", "access_roles", new[] { "scope", "organization_id", "workspace_id", "normalized_name" }, unique: true);
        migrationBuilder.CreateIndex("ix_access_roles_workspace_id", "access_roles", "workspace_id");
        migrationBuilder.CreateIndex("ix_access_groups_organization_id", "access_groups", "organization_id");
        migrationBuilder.CreateIndex("ix_access_groups_scope_organization_id_workspace_id_normalized_name", "access_groups", new[] { "scope", "organization_id", "workspace_id", "normalized_name" }, unique: true);
        migrationBuilder.CreateIndex("ix_access_groups_workspace_id", "access_groups", "workspace_id");

        migrationBuilder.Sql("CREATE TRIGGER trg_access_roles_delete_organization AFTER DELETE ON organizations BEGIN DELETE FROM access_roles WHERE organization_id = OLD.id; END;");
        migrationBuilder.Sql("CREATE TRIGGER trg_access_groups_delete_organization AFTER DELETE ON organizations BEGIN DELETE FROM access_groups WHERE organization_id = OLD.id; END;");
        migrationBuilder.Sql("CREATE TRIGGER trg_access_roles_delete_workspace AFTER DELETE ON workspaces BEGIN DELETE FROM access_roles WHERE workspace_id = OLD.id; END;");
        migrationBuilder.Sql("CREATE TRIGGER trg_access_groups_delete_workspace AFTER DELETE ON workspaces BEGIN DELETE FROM access_groups WHERE workspace_id = OLD.id; END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER trg_access_roles_delete_organization;");
        migrationBuilder.Sql("DROP TRIGGER trg_access_groups_delete_organization;");
        migrationBuilder.Sql("DROP TRIGGER trg_access_roles_delete_workspace;");
        migrationBuilder.Sql("DROP TRIGGER trg_access_groups_delete_workspace;");
        migrationBuilder.DropIndex("ix_access_roles_organization_id", "access_roles");
        migrationBuilder.DropIndex("ix_access_roles_scope_organization_id_workspace_id_normalized_name", "access_roles");
        migrationBuilder.DropIndex("ix_access_roles_workspace_id", "access_roles");
        migrationBuilder.DropIndex("ix_access_groups_organization_id", "access_groups");
        migrationBuilder.DropIndex("ix_access_groups_scope_organization_id_workspace_id_normalized_name", "access_groups");
        migrationBuilder.DropIndex("ix_access_groups_workspace_id", "access_groups");
        migrationBuilder.DropColumn("organization_id", "access_roles");
        migrationBuilder.DropColumn("scope", "access_roles");
        migrationBuilder.DropColumn("workspace_id", "access_roles");
        migrationBuilder.DropColumn("organization_id", "access_groups");
        migrationBuilder.DropColumn("scope", "access_groups");
        migrationBuilder.DropColumn("workspace_id", "access_groups");
        migrationBuilder.CreateIndex("ix_access_roles_normalized_name", "access_roles", "normalized_name", unique: true);
        migrationBuilder.CreateIndex("ix_access_groups_normalized_name", "access_groups", "normalized_name", unique: true);
    }
}
