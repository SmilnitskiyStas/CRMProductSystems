using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannedPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "price_lists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_lists", x => x.Id);
                    table.UniqueConstraint("AK_price_lists_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_price_lists_currency", "\"Currency\"='UAH'");
                    table.CheckConstraint("CK_price_lists_hash", "\"Fingerprint\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_price_lists_validity", "\"ValidUntil\" IS NULL OR \"ValidUntil\">\"ValidFrom\"");
                    table.CheckConstraint("CK_price_lists_version", "\"Version\">0 AND \"Priority\" BETWEEN 0 AND 10000");
                    table.ForeignKey(
                        name: "FK_price_lists_business_documents_TenantId_DocumentId",
                        columns: x => new { x.TenantId, x.DocumentId },
                        principalTable: "business_documents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_price_lists_locations_TenantId_LocationId",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_price_lists_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "price_resolution_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_resolution_snapshots", x => x.Id);
                    table.CheckConstraint("CK_price_resolution_snapshots_hash", "\"ContentHash\" ~ '^[0-9a-f]{64}$' AND \"RequestHash\" ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_price_resolution_snapshots_items_TenantId_ItemId",
                        columns: x => new { x.TenantId, x.ItemId },
                        principalTable: "items",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_price_resolution_snapshots_locations_TenantId_LocationId",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalTable: "locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "price_list_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ItemName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ItemFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_list_items", x => x.Id);
                    table.CheckConstraint("CK_price_list_items_hash", "\"ItemFingerprint\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_price_list_items_values", "\"Price\">=0 AND \"VatRate\" BETWEEN 0 AND 100 AND length(btrim(\"Unit\"))>0");
                    table.ForeignKey(
                        name: "FK_price_list_items_items_TenantId_ItemId",
                        columns: x => new { x.TenantId, x.ItemId },
                        principalTable: "items",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_price_list_items_price_lists_TenantId_PriceListId",
                        columns: x => new { x.TenantId, x.PriceListId },
                        principalTable: "price_lists",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_price_list_items_TenantId_ItemId",
                table: "price_list_items",
                columns: new[] { "TenantId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_price_list_items_TenantId_PriceListId_ItemId",
                table: "price_list_items",
                columns: new[] { "TenantId", "PriceListId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_price_lists_TenantId_DocumentId",
                table: "price_lists",
                columns: new[] { "TenantId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_price_lists_TenantId_ListKey_Version",
                table: "price_lists",
                columns: new[] { "TenantId", "ListKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_price_lists_TenantId_LocationId",
                table: "price_lists",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_price_resolution_snapshots_TenantId_CreatedBy_CreatedAt",
                table: "price_resolution_snapshots",
                columns: new[] { "TenantId", "CreatedBy", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_price_resolution_snapshots_TenantId_ItemId",
                table: "price_resolution_snapshots",
                columns: new[] { "TenantId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_price_resolution_snapshots_TenantId_LocationId",
                table: "price_resolution_snapshots",
                columns: new[] { "TenantId", "LocationId" });
            InstallGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Retain planned price history; rollback client activation or restore a reconciled backup.");
        }
    }
}
