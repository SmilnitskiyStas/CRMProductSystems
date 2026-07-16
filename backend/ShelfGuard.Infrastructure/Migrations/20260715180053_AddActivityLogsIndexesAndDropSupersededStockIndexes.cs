using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityLogsIndexesAndDropSupersededStockIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_activity_logs_created",
                table: "activity_logs",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_activity_logs_tenant_created",
                table: "activity_logs",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_activity_logs_tenant_user_created",
                table: "activity_logs",
                columns: new[] { "TenantId", "UserId", "CreatedAt" },
                descending: new[] { false, false, true });

            // ── Block 16 (pre-launch DB audit): drop 2 indexes on product_stock that were
            // never tracked by EF (added via raw SQL in earlier migrations, invisible to the
            // model snapshot) and have been fully superseded by an EF-tracked replacement
            // created in the *same* migration (AddPerformanceIndexes, 2026-06-18) that
            // introduced their replacements — every other table in that migration had its
            // old plain TenantId index dropped when the better one was added
            // (items/discounts/categories/pos_transactions/pos_transaction_items); these two
            // on product_stock were missed.
            //
            // idx_stock_tenant_store (TenantId, LocationId) is a strict prefix of
            // idx_stock_tenant_store_covering's key columns (same 2 leading columns, plus
            // INCLUDE Status/Quantity for index-only scans) — any query the plain index
            // could serve, the covering index serves at least as well.
            //
            // idx_stock_expiry_active (TenantId, LocationId, ProductId, ExpiryDate WHERE
            // Quantity>0 AND Status NOT IN ('sold_out','archived')) is the original
            // database-engineer.md canonical FEFO index (2026-06-04). idx_stock_fefo_active
            // (same 4 leading columns, WHERE Quantity>0 — a strict superset predicate) was
            // added 2026-06-18 as its intended replacement. The only query matching
            // idx_stock_expiry_active's exact predicate (StockRepository.GetFefoOrderedAsync,
            // the FEFO consumption path — "FEFO is sacred") is served just as correctly by
            // idx_stock_fefo_active plus a cheap in-index Status filter; dropping this does
            // NOT change FEFO query results, only removes a second B-tree Postgres has to
            // maintain on every stock write.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_stock_tenant_store;");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_stock_expiry_active;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_activity_logs_created",
                table: "activity_logs");

            migrationBuilder.DropIndex(
                name: "idx_activity_logs_tenant_created",
                table: "activity_logs");

            migrationBuilder.DropIndex(
                name: "idx_activity_logs_tenant_user_created",
                table: "activity_logs");

            migrationBuilder.Sql(@"
                CREATE INDEX idx_stock_tenant_store
                  ON product_stock(""TenantId"", ""LocationId"");
            ");
            migrationBuilder.Sql(@"
                CREATE INDEX idx_stock_expiry_active
                  ON product_stock(""TenantId"", ""LocationId"", ""ProductId"", ""ExpiryDate"")
                  WHERE ""Quantity"" > 0 AND ""Status"" NOT IN ('sold_out', 'archived');
            ");
        }
    }
}
