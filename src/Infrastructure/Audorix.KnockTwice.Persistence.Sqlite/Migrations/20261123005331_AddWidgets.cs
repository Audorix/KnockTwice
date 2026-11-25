using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddWidgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_visitor",
                table: "users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "widget_id",
                table: "conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_visitor",
                table: "conversation_participants",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "widgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    organization_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    title = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    welcome_message = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    accent_color = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    position = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    default_language = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    allow_anonymous = table.Column<bool>(type: "INTEGER", nullable: false),
                    allowed_origins = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    protected_secret = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_widgets", x => x.id);
                    table.ForeignKey(
                        name: "fk_widgets_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_widgets_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "visitors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    widget_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    external_subject = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visitors", x => x.id);
                    table.ForeignKey(
                        name: "fk_visitors_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_visitors_widgets_widget_id",
                        column: x => x.widget_id,
                        principalTable: "widgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "widget_responders",
                columns: table => new
                {
                    widget_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_widget_responders", x => new { x.widget_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_widget_responders_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_widget_responders_widgets_widget_id",
                        column: x => x.widget_id,
                        principalTable: "widgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visitor_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    visitor_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    widget_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    token_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visitor_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_visitor_sessions_visitors_visitor_id",
                        column: x => x.visitor_id,
                        principalTable: "visitors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_widget_id",
                table: "conversations",
                column: "widget_id");

            migrationBuilder.CreateIndex(
                name: "ix_visitor_sessions_token_hash",
                table: "visitor_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visitor_sessions_visitor_id",
                table: "visitor_sessions",
                column: "visitor_id");

            migrationBuilder.CreateIndex(
                name: "ix_visitors_user_id",
                table: "visitors",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visitors_widget_id_external_subject",
                table: "visitors",
                columns: new[] { "widget_id", "external_subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_widget_responders_user_id",
                table: "widget_responders",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_widgets_created_by_user_id",
                table: "widgets",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_widgets_organization_id",
                table: "widgets",
                column: "organization_id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversations_widgets_widget_id",
                table: "conversations",
                column: "widget_id",
                principalTable: "widgets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_conversations_widgets_widget_id",
                table: "conversations");

            migrationBuilder.DropTable(
                name: "visitor_sessions");

            migrationBuilder.DropTable(
                name: "widget_responders");

            migrationBuilder.DropTable(
                name: "visitors");

            migrationBuilder.DropTable(
                name: "widgets");

            migrationBuilder.DropIndex(
                name: "ix_conversations_widget_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "is_visitor",
                table: "users");

            migrationBuilder.DropColumn(
                name: "widget_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "is_visitor",
                table: "conversation_participants");
        }
    }
}
