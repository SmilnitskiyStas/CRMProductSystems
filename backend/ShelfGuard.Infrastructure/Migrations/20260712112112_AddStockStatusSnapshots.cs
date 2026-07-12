using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockStatusSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_status_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SafeCount = table.Column<int>(type: "integer", nullable: false),
                    WarningCount = table.Column<int>(type: "integer", nullable: false),
                    CriticalCount = table.Column<int>(type: "integer", nullable: false),
                    ExpiredCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_status_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_status_snapshots_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_stock_status_snapshots_tenant_date",
                table: "stock_status_snapshots",
                columns: new[] { "TenantId", "SnapshotDate" });

            migrationBuilder.CreateIndex(
                name: "idx_stock_status_snapshots_tenant_store_date",
                table: "stock_status_snapshots",
                columns: new[] { "TenantId", "LocationId", "SnapshotDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_status_snapshots_LocationId",
                table: "stock_status_snapshots",
                column: "LocationId");

            // ── RLS: stock_status_snapshots ─────────────────────────────────────
            // Standard tenant-isolation pattern (see legal_entities). NULLIF guard:
            // current_setting(..., true) returns '' after RESET, which breaks the
            // ::uuid cast without it.
            migrationBuilder.Sql(@"
                ALTER TABLE stock_status_snapshots ENABLE ROW LEVEL SECURITY;
                ALTER TABLE stock_status_snapshots FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON stock_status_snapshots
                  USING (""TenantId"" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                CREATE POLICY provider_bypass ON stock_status_snapshots
                  USING (current_setting('app.role', true) = 'provider');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON stock_status_snapshots;
                DROP POLICY IF EXISTS tenant_isolation ON stock_status_snapshots;
                ALTER TABLE stock_status_snapshots DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "stock_status_snapshots");
        }
    }
}
