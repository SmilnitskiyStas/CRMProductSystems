using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileCatalogLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mobile_catalog_locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingsId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_catalog_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_catalog_locations_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mobile_catalog_locations_mobile_catalog_settings_SettingsId",
                        column: x => x.SettingsId,
                        principalTable: "mobile_catalog_settings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_mobile_catalog_locations_tenant_location",
                table: "mobile_catalog_locations",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_catalog_locations_LocationId",
                table: "mobile_catalog_locations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "uq_mobile_catalog_locations_settings_location",
                table: "mobile_catalog_locations",
                columns: new[] { "SettingsId", "LocationId" },
                unique: true);

            // Preserve the behaviour of catalogs that existed before store targeting:
            // they remain available in every currently active store of their tenant.
            migrationBuilder.Sql(@"
                INSERT INTO mobile_catalog_locations (""Id"", ""TenantId"", ""SettingsId"", ""LocationId"")
                SELECT gen_random_uuid(), catalog.""TenantId"", catalog.""Id"", location.""Id""
                FROM mobile_catalog_settings AS catalog
                INNER JOIN locations AS location
                    ON location.""TenantId"" = catalog.""TenantId"" AND location.""IsActive"" = TRUE;

                ALTER TABLE mobile_catalog_locations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE mobile_catalog_locations FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON mobile_catalog_locations
                    USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON mobile_catalog_locations
                    USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON mobile_catalog_locations
                    USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mobile_catalog_locations");
        }
    }
}
