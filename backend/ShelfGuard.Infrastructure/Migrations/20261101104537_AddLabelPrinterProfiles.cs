using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelPrinterProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JobProtocol",
                table: "label_test_jobs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "label-job.v1");

            migrationBuilder.AddColumn<Guid>(
                name: "PrinterRevisionId",
                table: "label_test_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "label_printer_profile_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_printer_profile_revisions", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_label_printer_hash", "\"ContentHash\" ~ '^[0-9a-f]{64}$' AND \"RequestHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_label_printer_simulation", "(\"Payload\"->>'transport') IS NOT DISTINCT FROM 'simulation' AND COALESCE(\"Payload\"->>'protocol' IN ('pdf','zpl'),false)");
                    table.CheckConstraint("CK_label_printer_version", "\"Version\" BETWEEN 1 AND 10000");
                    table.ForeignKey(
                        name: "FK_label_printer_profile_revisions_locations_TenantId_Location~",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_label_test_jobs_TenantId_PrinterRevisionId",
                table: "label_test_jobs",
                columns: new[] { "TenantId", "PrinterRevisionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_label_test_job_printer_protocol",
                table: "label_test_jobs",
                sql: "(\"JobProtocol\"='label-job.v1' AND \"PrinterRevisionId\" IS NULL) OR (\"JobProtocol\"='label-job.v2' AND \"PrinterRevisionId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_label_printer_profile_revisions_TenantId_LocationId_Profile~",
                table: "label_printer_profile_revisions",
                columns: new[] { "TenantId", "LocationId", "ProfileKey" });

            migrationBuilder.CreateIndex(
                name: "IX_label_printer_profile_revisions_TenantId_ProfileKey_Version",
                table: "label_printer_profile_revisions",
                columns: new[] { "TenantId", "ProfileKey", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_label_test_jobs_label_printer_profile_revisions_TenantId_Pr~",
                table: "label_test_jobs",
                columns: new[] { "TenantId", "PrinterRevisionId" },
                principalTable: "label_printer_profile_revisions",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            InstallGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Preserve printer profile and job provenance; use a forward migration.");
    }
}
