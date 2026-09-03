using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Supplier-portal expansion — Phase 3 (plan `1-partitioned-book.md`, decision D4).
    ///
    /// <c>marketplace_order_item_batches</c> — the supplier's per-line batch allocation, written
    /// when an order ships. One row does two jobs: it is the supplier-side stock-consumption
    /// ledger entry (which <c>supplier_stock</c> batch left the warehouse), and it is the
    /// hand-off record the CLIENT reads to prefill its own receiving draft with one sub-row per
    /// batch (amends ADR-033: <c>marketplace_order_receipt_items</c> becomes 1→N per order line).
    ///
    /// RLS — the MIRROR IMAGE of ADR-033's receipt split, and the only table in this feature area
    /// pointing that way:
    ///   • <c>tenant_isolation</c> (FOR ALL + WITH CHECK) on <c>SupplierTenantId</c> — the
    ///     supplier writes, because the supplier is the party that picks and ships.
    ///   • <c>client_read</c> (FOR SELECT only) on <c>ClientTenantId</c> — the client may read
    ///     its own orders' allocations to prefill a receipt, and may never write them.
    ///   • <c>provider_bypass</c> IN ('provider','provider_admin') + <c>worker_bypass</c>, both
    ///     from day one (feedback-rls-worker-bypass-missing / TASK-343 lesson).
    /// Both policies are PERMISSIVE, so they OR together for SELECT and the write path stays
    /// supplier-only. The audit test in RlsCrossTenantIntegrationTests requires the literal
    /// policy names tenant_isolation / provider_bypass / worker_bypass — satisfied; the extra
    /// <c>client_read</c> is additive, exactly as <c>supplier_read</c> is on
    /// <c>marketplace_order_receipts</c> (20260821151649_AddMarketplaceOrderReceiving).
    ///
    /// Also here: <c>marketplace_orders.SourceWarehouseId</c> (one source warehouse per order —
    /// partial / multi-warehouse fulfilment is out of v1 scope) and
    /// <c>marketplace_order_receipt_items.SourceOrderItemBatchId</c> (which allocation a
    /// prefilled receipt sub-row came from). Both nullable + no backfill: legacy orders and
    /// module-off shipments have no source warehouse and no batches.
    /// </summary>
    public partial class AddMarketplaceOrderItemBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceWarehouseId",
                table: "marketplace_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceOrderItemBatchId",
                table: "marketplace_order_receipt_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "marketplace_order_item_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierStockId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Qty = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketplace_order_item_batches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_marketplace_order_item_batches_marketplace_order_items_Orde~",
                        column: x => x.OrderItemId,
                        principalTable: "marketplace_order_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_marketplace_order_item_batches_supplier_stock_SupplierStock~",
                        column: x => x.SupplierStockId,
                        principalTable: "supplier_stock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_orders_SourceWarehouseId",
                table: "marketplace_orders",
                column: "SourceWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_receipt_items_SourceOrderItemBatchId",
                table: "marketplace_order_receipt_items",
                column: "SourceOrderItemBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_item_batches_OrderId",
                table: "marketplace_order_item_batches",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_item_batches_OrderItemId",
                table: "marketplace_order_item_batches",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_item_batches_SupplierStockId",
                table: "marketplace_order_item_batches",
                column: "SupplierStockId");

            migrationBuilder.AddForeignKey(
                name: "FK_marketplace_order_receipt_items_marketplace_order_item_batc~",
                table: "marketplace_order_receipt_items",
                column: "SourceOrderItemBatchId",
                principalTable: "marketplace_order_item_batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_marketplace_orders_locations_SourceWarehouseId",
                table: "marketplace_orders",
                column: "SourceWarehouseId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── RLS: marketplace_order_item_batches (split, INVERTED vs ADR-033) ──
            // Supplier writes / client reads. See the class summary above for the full
            // rationale; NULLIF-guarded and fail-closed (no `IS NULL OR` branch) on both.
            migrationBuilder.Sql(@"
                ALTER TABLE marketplace_order_item_batches ENABLE ROW LEVEL SECURITY;
                ALTER TABLE marketplace_order_item_batches FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON marketplace_order_item_batches
                  USING (""SupplierTenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
                  WITH CHECK (""SupplierTenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

                CREATE POLICY client_read ON marketplace_order_item_batches
                  FOR SELECT
                  USING (""ClientTenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

                CREATE POLICY provider_bypass ON marketplace_order_item_batches
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));

                CREATE POLICY worker_bypass ON marketplace_order_item_batches
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS worker_bypass ON marketplace_order_item_batches;
                DROP POLICY IF EXISTS provider_bypass ON marketplace_order_item_batches;
                DROP POLICY IF EXISTS client_read ON marketplace_order_item_batches;
                DROP POLICY IF EXISTS tenant_isolation ON marketplace_order_item_batches;
                ALTER TABLE marketplace_order_item_batches DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_marketplace_order_receipt_items_marketplace_order_item_batc~",
                table: "marketplace_order_receipt_items");

            migrationBuilder.DropForeignKey(
                name: "FK_marketplace_orders_locations_SourceWarehouseId",
                table: "marketplace_orders");

            migrationBuilder.DropTable(
                name: "marketplace_order_item_batches");

            migrationBuilder.DropIndex(
                name: "IX_marketplace_orders_SourceWarehouseId",
                table: "marketplace_orders");

            migrationBuilder.DropIndex(
                name: "IX_marketplace_order_receipt_items_SourceOrderItemBatchId",
                table: "marketplace_order_receipt_items");

            migrationBuilder.DropColumn(
                name: "SourceWarehouseId",
                table: "marketplace_orders");

            migrationBuilder.DropColumn(
                name: "SourceOrderItemBatchId",
                table: "marketplace_order_receipt_items");
        }
    }
}
