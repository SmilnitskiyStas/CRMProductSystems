using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// New table <c>supplier_metrics_snapshots</c> (TASK-670) — append-only daily copy of a
    /// supplier's aggregate metrics, written by the nightly supplier-metrics worker job
    /// (idempotent upsert on the unique <c>(SupplierId, SnapshotDate)</c> index). Feeds the
    /// buyer-facing metric trend-chart detail page.
    ///
    /// New table — RLS triad (tenant_isolation + provider_bypass + worker_bypass) added
    /// explicitly since new tables don't auto-inherit (feedback-rls-worker-bypass-missing).
    /// Policy SQL copied verbatim from the current <c>supplier_metrics</c> policies
    /// (V4SupplierMarketplace region + the NULLIF-guard fix
    /// 20260714180000_FixFailOpenTenantIsolationOnReset + the provider-bypass expansion
    /// 20260714150000_ExpandProviderBypassToProviderAdmin). No WITH CHECK clauses — the other
    /// supplier_* tables carry none.
    /// </summary>
    public partial class AddSupplierMetricsHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_metrics_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AvgDeliveryDays = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    OrderAccuracy = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    QualityScore = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    Rating = table.Column<decimal>(type: "numeric(3,2)", nullable: true),
                    CancellationRate = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    ResponseTimeHours = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    DeliverySampleSize = table.Column<int>(type: "integer", nullable: true),
                    ResponseSampleSize = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_metrics_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_metrics_snapshots_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_metrics_snapshots_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Idempotent upsert key — worker does ON CONFLICT (SupplierId, SnapshotDate).
            // Also serves the buyer history query (WHERE SupplierId = ? ORDER BY SnapshotDate
            // DESC) via a backward b-tree index scan — no dedicated DESC index needed.
            migrationBuilder.CreateIndex(
                name: "idx_supplier_metrics_snapshots_supplier_date",
                table: "supplier_metrics_snapshots",
                columns: new[] { "SupplierId", "SnapshotDate" },
                unique: true);

            // Leading index on the RLS tenant column (Block 16 audit rule).
            migrationBuilder.CreateIndex(
                name: "IX_supplier_metrics_snapshots_TenantId",
                table: "supplier_metrics_snapshots",
                column: "TenantId");

            // ── RLS: supplier_metrics_snapshots ────────────────────────────────
            // New table — the triad does NOT auto-inherit; add all three explicitly in the
            // same migration that enables FORCE RLS (feedback-rls-worker-bypass-missing).
            // Verbatim from the live supplier_metrics policies (no WITH CHECK).
            migrationBuilder.Sql(@"
                ALTER TABLE supplier_metrics_snapshots ENABLE ROW LEVEL SECURITY;
                ALTER TABLE supplier_metrics_snapshots FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON supplier_metrics_snapshots
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON supplier_metrics_snapshots
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON supplier_metrics_snapshots
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS worker_bypass ON supplier_metrics_snapshots;
                DROP POLICY IF EXISTS provider_bypass ON supplier_metrics_snapshots;
                DROP POLICY IF EXISTS tenant_isolation ON supplier_metrics_snapshots;
                ALTER TABLE supplier_metrics_snapshots DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "supplier_metrics_snapshots");
        }
    }
}
