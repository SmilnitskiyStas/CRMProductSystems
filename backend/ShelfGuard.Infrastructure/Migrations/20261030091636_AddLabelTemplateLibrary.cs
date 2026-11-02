using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelTemplateLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "label_template_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_template_revisions", x => x.Id);
                    table.UniqueConstraint("AK_label_template_revisions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_label_template_hash", "\"ContentHash\" ~ '^[0-9a-f]{64}$' AND \"RequestHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_label_template_version", "\"Version\" BETWEEN 1 AND 10000");
                    table.ForeignKey(
                        name: "FK_label_template_revisions_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "label_store_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_store_assignments", x => x.Id);
                    table.CheckConstraint("CK_label_assignment_hash", "\"RequestHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_label_assignment_version", "\"Version\">0");
                    table.ForeignKey(
                        name: "FK_label_store_assignments_label_template_revisions_TenantId_R~",
                        columns: x => new { x.TenantId, x.RevisionId },
                        principalTable: "label_template_revisions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_label_store_assignments_locations_TenantId_LocationId",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_label_store_assignments_TenantId_LocationId_Version",
                table: "label_store_assignments",
                columns: new[] { "TenantId", "LocationId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_label_store_assignments_TenantId_RevisionId",
                table: "label_store_assignments",
                columns: new[] { "TenantId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_label_template_revisions_TenantId_TemplateKey_Version",
                table: "label_template_revisions",
                columns: new[] { "TenantId", "TemplateKey", "Version" },
                unique: true);
            InstallGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Preserve label template history; use a forward migration.");
    }
}
