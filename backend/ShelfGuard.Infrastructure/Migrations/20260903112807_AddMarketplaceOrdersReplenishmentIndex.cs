using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Supplier-portal expansion Phase 4 (plan 1-partitioned-book.md, D5 / п.2). Pure DDL — no
    /// new tables, no columns, no RLS changes.
    ///
    /// Adds one partial index backing <c>OrderCalcRepository.GetOpenMarketplaceInTransitAsync</c>:
    /// the replenishment engine now folds OPEN B2B marketplace orders (status new/confirmed/shipped)
    /// headed to a store into its "in transit" figure so it stops recommending goods the buyer has
    /// already ordered (the double-order bug). The hot lookup is
    /// <c>WHERE "DestinationStoreId" = @store AND "Status" IN ('new','confirmed','shipped')</c>,
    /// scoped per client tenant by the OR-based <c>tenant_isolation</c> RLS.
    ///
    ///   ix_marketplace_orders_open_by_dest
    ///     ("ClientTenantId", "DestinationStoreId", "Status")
    ///     WHERE "Status" IN ('new','confirmed','shipped')
    ///
    /// Hand-written raw SQL (same treatment as ix_marketplace_orders_metrics from
    /// AddSupplierPerformanceData): partial indexes are not tracked in the EF model snapshot.
    /// </summary>
    public partial class AddMarketplaceOrdersReplenishmentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_marketplace_orders_open_by_dest
                  ON marketplace_orders (""ClientTenantId"", ""DestinationStoreId"", ""Status"")
                  WHERE ""Status"" IN ('new', 'confirmed', 'shipped');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_marketplace_orders_open_by_dest;");
        }
    }
}
