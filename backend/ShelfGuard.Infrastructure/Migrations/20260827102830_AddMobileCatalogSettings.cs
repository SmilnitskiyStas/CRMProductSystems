using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileCatalogSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mobile_catalog_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    BannerUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LayoutMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PublishAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UnpublishAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_catalog_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_catalog_settings_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mobile_catalog_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingsId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    MobileDiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_catalog_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_catalog_items_items_ProductId",
                        column: x => x.ProductId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mobile_catalog_items_mobile_catalog_settings_SettingsId",
                        column: x => x.SettingsId,
                        principalTable: "mobile_catalog_settings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_mobile_catalog_items_tenant_order",
                table: "mobile_catalog_items",
                columns: new[] { "TenantId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_catalog_items_ProductId",
                table: "mobile_catalog_items",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "uq_mobile_catalog_items_settings_product",
                table: "mobile_catalog_items",
                columns: new[] { "SettingsId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_mobile_catalog_settings_tenant",
                table: "mobile_catalog_settings",
                column: "TenantId",
                unique: true);

            migrationBuilder.Sql(@"
                ALTER TABLE mobile_catalog_settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE mobile_catalog_settings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON mobile_catalog_settings USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON mobile_catalog_settings USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON mobile_catalog_settings USING (current_setting('app.role', true) = 'worker');

                ALTER TABLE mobile_catalog_items ENABLE ROW LEVEL SECURITY;
                ALTER TABLE mobile_catalog_items FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON mobile_catalog_items USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON mobile_catalog_items USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON mobile_catalog_items USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mobile_catalog_items");

            migrationBuilder.DropTable(
                name: "mobile_catalog_settings");
        }
    }
}
