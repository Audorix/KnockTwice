using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.ChatX.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    scope = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    organization_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    direct_key = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    last_activity_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    last_sequence = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                    table.CheckConstraint("ck_conversations_direct_key", "(type = 'Direct' AND direct_key IS NOT NULL) OR (type <> 'Direct' AND direct_key IS NULL)");
                    table.CheckConstraint("ck_conversations_scope", "(scope = 'Personal' AND organization_id IS NULL) OR (scope = 'Organization' AND organization_id IS NOT NULL)");
                    table.CheckConstraint("ck_conversations_sequence", "last_sequence >= 0");
                    table.ForeignKey(
                        name: "fk_conversations_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversations_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversation_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    conversation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    subject_user_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    text = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_events", x => x.id);
                    table.CheckConstraint("ck_conversation_events_sequence", "sequence >= 1");
                    table.ForeignKey(
                        name: "fk_conversation_events_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_events_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversation_events_users_subject_user_id",
                        column: x => x.subject_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversation_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    conversation_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    joined_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    left_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    visible_from_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    last_read_sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    muted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_participants", x => x.id);
                    table.UniqueConstraint("ak_conversation_participants_conversation_id_user_id", x => new { x.conversation_id, x.user_id });
                    table.CheckConstraint("ck_conversation_participants_read", "last_read_sequence >= visible_from_sequence - 1");
                    table.CheckConstraint("ck_conversation_participants_visible_from", "visible_from_sequence >= 1");
                    table.ForeignKey(
                        name: "fk_conversation_participants_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_participants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_events_actor_user_id",
                table: "conversation_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_events_subject_user_id",
                table: "conversation_events",
                column: "subject_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_conversation_events_sequence",
                table: "conversation_events",
                columns: new[] { "conversation_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_participants_user_id_status",
                table: "conversation_participants",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_created_by_user_id",
                table: "conversations",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_organization_id",
                table: "conversations",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ux_conversations_direct_key",
                table: "conversations",
                column: "direct_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_events");

            migrationBuilder.DropTable(
                name: "conversation_participants");

            migrationBuilder.DropTable(
                name: "conversations");
        }
    }
}
