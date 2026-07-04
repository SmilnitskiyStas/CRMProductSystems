using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierItemUniversalFieldsAndChildTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "supplier_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DepthCm",
                table: "supplier_items",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossWeightKg",
                table: "supplier_items",
                type: "numeric(10,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HeightCm",
                table: "supplier_items",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Manufacturer",
                table: "supplier_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManufacturerCountry",
                table: "supplier_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxQty",
                table: "supplier_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WidthCm",
                table: "supplier_items",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "supplier_item_barcodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SupplierItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Barcode = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false, defaultValue: "primary"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_item_barcodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_item_barcodes_supplier_items_SupplierItemId",
                        column: x => x.SupplierItemId,
                        principalTable: "supplier_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_item_barcodes_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_item_images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SupplierItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false, defaultValue: "gallery"),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_item_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_item_images_supplier_items_SupplierItemId",
                        column: x => x.SupplierItemId,
                        principalTable: "supplier_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_item_images_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_item_barcodes_SupplierItemId_Barcode",
                table: "supplier_item_barcodes",
                columns: new[] { "SupplierItemId", "Barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_item_barcodes_TenantId",
                table: "supplier_item_barcodes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_item_images_SupplierItemId",
                table: "supplier_item_images",
                column: "SupplierItemId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_item_images_TenantId",
                table: "supplier_item_images",
                column: "TenantId");

            // ── RLS: supplier_item_barcodes ─────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_item_barcodes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE supplier_item_barcodes FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_item_barcodes
                  USING (
                    NULLIF(current_setting('app.tenant_id', true), '') IS NULL
                    OR ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
                  );
                CREATE POLICY provider_bypass ON supplier_item_barcodes
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── RLS: supplier_item_images ────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_item_images ENABLE ROW LEVEL SECURITY;
                ALTER TABLE supplier_item_images FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_item_images
                  USING (
                    NULLIF(current_setting('app.tenant_id', true), '') IS NULL
                    OR ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
                  );
                CREATE POLICY provider_bypass ON supplier_item_images
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_item_barcodes");

            migrationBuilder.DropTable(
                name: "supplier_item_images");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "supplier_items");

            migrationBuilder.DropColumn(
                name: "DepthCm",
                table: "supplier_items");

            migrationBuilder.DropColumn(
                name: "GrossWeightKg",
                table: "supplier_items");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                table: "supplier_items");

            migrationBuilder.DropColumn(
                name: "Manufacturer",
                table: "supplier_items");

            migrationBuilder.DropColumn(
                name: "ManufacturerCountry",
                table: "supplier_items");

            migrationBuilder.DropColumn(
                name: "MaxQty",
                table: "supplier_items");

            migrationBuilder.DropColumn(
                name: "WidthCm",
                table: "supplier_items");
        }
    }
}
