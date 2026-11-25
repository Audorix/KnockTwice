using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "email_notifications",
                table: "user_profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "batched");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_notification_email_at_utc",
                table: "user_profiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "last_notified_sequence",
                table: "conversation_participants",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // Existing participants count as notified up to now: upgrading never emails old unread messages.
            migrationBuilder.Sql(
                "UPDATE conversation_participants SET last_notified_sequence = (SELECT c.last_sequence FROM conversations c WHERE c.id = conversation_participants.conversation_id);");

            migrationBuilder.CreateTable(
                name: "push_queue",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    entry_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    enqueued_at_ticks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_queue", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    session_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    endpoint = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    p256dh = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    auth = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    last_success_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    failures = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_push_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "web_push_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    public_key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    protected_private_key = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_web_push_keys", x => x.id);
                    table.CheckConstraint("ck_web_push_keys_single", "id = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_endpoint",
                table: "push_subscriptions",
                column: "endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_user_id",
                table: "push_subscriptions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_queue");

            migrationBuilder.DropTable(
                name: "push_subscriptions");

            migrationBuilder.DropTable(
                name: "web_push_keys");

            migrationBuilder.DropColumn(
                name: "email_notifications",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "last_notification_email_at_utc",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "last_notified_sequence",
                table: "conversation_participants");
        }
    }
}
