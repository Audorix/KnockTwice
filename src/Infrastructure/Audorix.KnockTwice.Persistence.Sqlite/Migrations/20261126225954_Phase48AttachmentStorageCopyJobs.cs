using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audorix.KnockTwice.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Phase48AttachmentStorageCopyJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attachment_storage_migration_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    destination_provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    lease_token = table.Column<Guid>(type: "TEXT", nullable: true),
                    lease_expires_at_ticks = table.Column<long>(type: "INTEGER", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachment_storage_migration_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "attachment_storage_migration_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    job_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    attachment_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    storage_key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    file_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    size = table.Column<long>(type: "INTEGER", nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    available_at_ticks = table.Column<long>(type: "INTEGER", nullable: false),
                    lease_token = table.Column<Guid>(type: "TEXT", nullable: true),
                    lease_expires_at_ticks = table.Column<long>(type: "INTEGER", nullable: true),
                    copied_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachment_storage_migration_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_attachment_storage_migration_files_attachment_storage_migra~",
                        column: x => x.job_id,
                        principalTable: "attachment_storage_migration_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachment_storage_migration_files_attachment",
                table: "attachment_storage_migration_files",
                column: "attachment_id");

            migrationBuilder.CreateIndex(
                name: "ix_attachment_storage_migration_files_claim",
                table: "attachment_storage_migration_files",
                columns: new[] { "job_id", "state", "available_at_ticks" });

            migrationBuilder.CreateIndex(
                name: "ix_attachment_storage_migration_jobs_claim",
                table: "attachment_storage_migration_jobs",
                columns: new[] { "state", "lease_expires_at_ticks" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachment_storage_migration_files");

            migrationBuilder.DropTable(
                name: "attachment_storage_migration_jobs");
        }
    }
}
