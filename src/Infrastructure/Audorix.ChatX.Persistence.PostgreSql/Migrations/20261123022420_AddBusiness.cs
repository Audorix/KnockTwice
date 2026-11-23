using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.ChatX.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddBusiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "channel_creation",
                table: "organizations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Members");

            migrationBuilder.AddColumn<bool>(
                name: "guests_enabled",
                table: "organizations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_support_agent",
                table: "organization_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_to_user_id",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "channel_key",
                table: "conversations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "support_status",
                table: "conversations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "visibility",
                table: "conversations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_guest",
                table: "conversation_participants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "guest_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    responded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guest_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_guest_invitations_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_guest_invitations_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_guest_invitations_users_invited_by_user_id",
                        column: x => x.invited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "organization_guests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    joined_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_guests", x => x.id);
                    table.UniqueConstraint("ak_organization_guests_organization_id_user_id", x => new { x.organization_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_organization_guests_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_organization_guests_users_invited_by_user_id",
                        column: x => x.invited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_organization_guests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                    table.ForeignKey(
                        name: "fk_teams_workspaces_organization_id_workspace_id",
                        columns: x => new { x.organization_id, x.workspace_id },
                        principalTable: "workspaces",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "channel_teams",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_teams", x => new { x.conversation_id, x.team_id });
                    table.ForeignKey(
                        name: "fk_channel_teams_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_channel_teams_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    joined_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_members", x => x.id);
                    table.UniqueConstraint("ak_team_members_team_id_user_id", x => new { x.team_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_team_members_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_team_members_workspace_members_workspace_id_user_id",
                        columns: x => new { x.workspace_id, x.user_id },
                        principalTable: "workspace_members",
                        principalColumns: new[] { "workspace_id", "user_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_assigned_to_user_id",
                table: "conversations",
                column: "assigned_to_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_organization_id_type_support_status",
                table: "conversations",
                columns: new[] { "organization_id", "type", "support_status" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_organization_id_workspace_id",
                table: "conversations",
                columns: new[] { "organization_id", "workspace_id" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_workspace_id",
                table: "conversations",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ux_conversations_channel_key",
                table: "conversations",
                column: "channel_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_channel_teams_team_id",
                table: "channel_teams",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_guest_invitations_conversation_id",
                table: "guest_invitations",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_guest_invitations_invited_by_user_id",
                table: "guest_invitations",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_guest_invitations_normalized_email_status",
                table: "guest_invitations",
                columns: new[] { "normalized_email", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_guest_invitations_organization_id_status",
                table: "guest_invitations",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_organization_guests_invited_by_user_id",
                table: "organization_guests",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_organization_guests_user_id_status",
                table: "organization_guests",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_team_members_workspace_id_user_id",
                table: "team_members",
                columns: new[] { "workspace_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_teams_organization_id_workspace_id",
                table: "teams",
                columns: new[] { "organization_id", "workspace_id" });

            migrationBuilder.CreateIndex(
                name: "ix_teams_workspace_id",
                table: "teams",
                column: "workspace_id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversations_users_assigned_to_user_id",
                table: "conversations",
                column: "assigned_to_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_conversations_workspaces_organization_id_workspace_id",
                table: "conversations",
                columns: new[] { "organization_id", "workspace_id" },
                principalTable: "workspaces",
                principalColumns: new[] { "organization_id", "id" },
                onDelete: ReferentialAction.Cascade);

            // Website conversations created before Phase 11 start in the support inbox as open.
            migrationBuilder.Sql("UPDATE conversations SET support_status = 'Open' WHERE type = 'Website' AND support_status IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_conversations_users_assigned_to_user_id",
                table: "conversations");

            migrationBuilder.DropForeignKey(
                name: "fk_conversations_workspaces_organization_id_workspace_id",
                table: "conversations");

            migrationBuilder.DropTable(
                name: "channel_teams");

            migrationBuilder.DropTable(
                name: "guest_invitations");

            migrationBuilder.DropTable(
                name: "organization_guests");

            migrationBuilder.DropTable(
                name: "team_members");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropIndex(
                name: "ix_conversations_assigned_to_user_id",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "ix_conversations_organization_id_type_support_status",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "ix_conversations_organization_id_workspace_id",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "ix_conversations_workspace_id",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "ux_conversations_channel_key",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "channel_creation",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "guests_enabled",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "is_support_agent",
                table: "organization_members");

            migrationBuilder.DropColumn(
                name: "assigned_to_user_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "channel_key",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "support_status",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "visibility",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "is_guest",
                table: "conversation_participants");
        }
    }
}
