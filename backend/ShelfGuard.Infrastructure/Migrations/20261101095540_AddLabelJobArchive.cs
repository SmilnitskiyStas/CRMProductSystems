using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelJobArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "label_test_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientCreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CommandHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IntentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ArtifactHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Copies = table.Column<int>(type: "integer", nullable: false),
                    RequiresBulk = table.Column<bool>(type: "boolean", nullable: false),
                    HasPrice = table.Column<bool>(type: "boolean", nullable: false),
                    InitialPayload = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_test_jobs", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_label_test_job_copies", "\"Copies\" BETWEEN 1 AND 20 AND (\"Copies\"=1 OR \"RequiresBulk\")");
                    table.CheckConstraint("CK_label_test_job_hashes", "\"CommandHash\" ~ '^[0-9a-f]{64}$' AND \"IntentHash\" ~ '^[0-9a-f]{64}$' AND \"ArtifactHash\" ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_label_test_jobs_locations_TenantId_LocationId",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "label_test_job_events",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seq = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReportedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false),
                    CheckpointPayload = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_test_job_events", x => new { x.TenantId, x.JobId, x.Seq });
                    table.CheckConstraint("CK_label_test_job_event", "\"Seq\" BETWEEN 1 AND 16 AND \"Attempt\" BETWEEN 0 AND 3 AND \"Status\" IN ('queued','running','unknown','failed','succeeded','cancelled')");
                    table.CheckConstraint("CK_label_test_job_event_hash", "\"ContentHash\" ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_label_test_job_events_label_test_jobs_TenantId_JobId",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "label_test_jobs",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "label_test_job_uploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Result = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_test_job_uploads", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_label_test_job_upload_hash", "\"RequestHash\" ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_label_test_job_uploads_label_test_jobs_TenantId_JobId",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "label_test_jobs",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "label_test_job_receipts",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventSeq = table.Column<int>(type: "integer", nullable: false),
                    ClientAcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CommandHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ArtifactHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Copies = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_test_job_receipts", x => new { x.TenantId, x.JobId });
                    table.CheckConstraint("CK_label_test_job_receipt_hashes", "\"CommandHash\" ~ '^[0-9a-f]{64}$' AND \"ArtifactHash\" ~ '^[0-9a-f]{64}$' AND \"Copies\" BETWEEN 1 AND 20");
                    table.ForeignKey(
                        name: "FK_label_test_job_receipts_label_test_job_events_TenantId_JobI~",
                        columns: x => new { x.TenantId, x.JobId, x.EventSeq },
                        principalTable: "label_test_job_events",
                        principalColumns: new[] { "TenantId", "JobId", "Seq" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_label_test_job_receipts_TenantId_JobId_EventSeq",
                table: "label_test_job_receipts",
                columns: new[] { "TenantId", "JobId", "EventSeq" });

            migrationBuilder.CreateIndex(
                name: "IX_label_test_job_uploads_TenantId_JobId",
                table: "label_test_job_uploads",
                columns: new[] { "TenantId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_label_test_jobs_TenantId_LocationId_ReceivedAt_Id",
                table: "label_test_jobs",
                columns: new[] { "TenantId", "LocationId", "ReceivedAt", "Id" });
            InstallGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Preserve label job audit; use a forward migration.");
    }
}
