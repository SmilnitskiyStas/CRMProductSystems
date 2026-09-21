using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemZoneAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "item_zone_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_zone_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_item_zone_assignments_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_item_zone_assignments_location_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "location_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_item_zone_assignments_tenant_zone",
                table: "item_zone_assignments",
                columns: new[] { "TenantId", "ZoneId" });

            migrationBuilder.CreateIndex(
                name: "IX_item_zone_assignments_ItemId_ZoneId",
                table: "item_zone_assignments",
                columns: new[] { "ItemId", "ZoneId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_item_zone_assignments_ZoneId",
                table: "item_zone_assignments",
                column: "ZoneId");

            // Catalog metadata (product ↔ zone tags), same RLS category as items/location_zones
            // themselves — tenant-isolated, no store_scope restrictive policy needed.
            migrationBuilder.Sql(@"
                ALTER TABLE item_zone_assignments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE item_zone_assignments FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON item_zone_assignments
                    USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON item_zone_assignments
                    USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON item_zone_assignments
                    USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_zone_assignments");
        }
    }
}
