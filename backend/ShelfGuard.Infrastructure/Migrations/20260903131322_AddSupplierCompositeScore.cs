using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Supplier-portal expansion — Phase 6d (plan <c>1-partitioned-book.md</c>, request #10):
    /// composite quality score. Adds <c>CompositeScore numeric(4,3)</c> and
    /// <c>OnTimeDeliveryRate numeric(5,4)</c>, both nullable, to <c>supplier_metrics</c> AND its
    /// append-only history table <c>supplier_metrics_snapshots</c>. Both are worker-computed by
    /// <c>supplier-metrics-recompute.job.ts</c> (amends ADR-036 Decision 4 — the disjoint
    /// write-boundary column set grows by these two; still a separate <c>UPDATE</c>, still no
    /// column shared with the synchronous <c>Rating</c> writer).
    ///
    /// <para>NO RLS change: both tables already carry the
    /// <c>tenant_isolation</c> + <c>provider_bypass</c> + <c>worker_bypass</c> triad (FORCE) and the
    /// new columns inherit it. No new table, so the RLS audit surface is unchanged.</para>
    /// </summary>
    public partial class AddSupplierCompositeScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CompositeScore",
                table: "supplier_metrics_snapshots",
                type: "numeric(4,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OnTimeDeliveryRate",
                table: "supplier_metrics_snapshots",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CompositeScore",
                table: "supplier_metrics",
                type: "numeric(4,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OnTimeDeliveryRate",
                table: "supplier_metrics",
                type: "numeric(5,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompositeScore",
                table: "supplier_metrics_snapshots");

            migrationBuilder.DropColumn(
                name: "OnTimeDeliveryRate",
                table: "supplier_metrics_snapshots");

            migrationBuilder.DropColumn(
                name: "CompositeScore",
                table: "supplier_metrics");

            migrationBuilder.DropColumn(
                name: "OnTimeDeliveryRate",
                table: "supplier_metrics");
        }
    }
}
