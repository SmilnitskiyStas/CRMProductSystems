using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceOrderReceiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DestinationStoreId",
                table: "marketplace_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "marketplace_order_receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    MarketplaceOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "draft"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketplace_order_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_marketplace_order_receipts_locations_DestinationStoreId",
                        column: x => x.DestinationStoreId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_marketplace_order_receipts_marketplace_orders_MarketplaceOr~",
                        column: x => x.MarketplaceOrderId,
                        principalTable: "marketplace_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_marketplace_order_receipts_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_marketplace_order_receipts_users_ReceivedByUserId",
                        column: x => x.ReceivedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "marketplace_order_receipt_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplaceOrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemNameSnapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    QuantityOrdered = table.Column<decimal>(type: "numeric(12,3)", nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "numeric(12,3)", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DiscrepancyNotes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketplace_order_receipt_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_marketplace_order_receipt_items_items_ProductId",
                        column: x => x.ProductId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_marketplace_order_receipt_items_marketplace_order_items_Mar~",
                        column: x => x.MarketplaceOrderItemId,
                        principalTable: "marketplace_order_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_marketplace_order_receipt_items_marketplace_order_receipts_~",
                        column: x => x.ReceiptId,
                        principalTable: "marketplace_order_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_orders_DestinationStoreId",
                table: "marketplace_orders",
                column: "DestinationStoreId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_receipt_items_MarketplaceOrderItemId",
                table: "marketplace_order_receipt_items",
                column: "MarketplaceOrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_receipt_items_ProductId",
                table: "marketplace_order_receipt_items",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_receipt_items_ReceiptId",
                table: "marketplace_order_receipt_items",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_receipts_ClientTenantId",
                table: "marketplace_order_receipts",
                column: "ClientTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_receipts_CreatedByUserId",
                table: "marketplace_order_receipts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_receipts_DestinationStoreId",
                table: "marketplace_order_receipts",
                column: "DestinationStoreId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_receipts_MarketplaceOrderId",
                table: "marketplace_order_receipts",
                column: "MarketplaceOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_receipts_ReceivedByUserId",
                table: "marketplace_order_receipts",
                column: "ReceivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_marketplace_order_receipts_SupplierTenantId",
                table: "marketplace_order_receipts",
                column: "SupplierTenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_marketplace_orders_locations_DestinationStoreId",
                table: "marketplace_orders",
                column: "DestinationStoreId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── RLS: marketplace_order_receipts ─────────────────────────────
            // TASK-586, ADR-033 Decision 3: genuinely different shape from every other
            // two-tenant table in this feature area. marketplace_orders/marketplace_order_items
            // use one OR-based tenant_isolation policy with no FOR clause, which grants BOTH
            // tenants full read/write — correct there (both sides legitimately write). Wrong
            // here: this is the client's physical confirmation of receipt, the supplier must
            // never get write access. So: tenant_isolation (client, FOR ALL, WITH CHECK) +
            // supplier_read (FOR SELECT only) as two separate named policies, not a copy of the
            // marketplace_orders pattern. provider_bypass uses the current
            // IN ('provider', 'provider_admin') form (20260714150000_ExpandProviderBypassTo
            // ProviderAdmin is the live source of truth per database-schema.md — the stale
            // single-value template at the top of that doc predates it). worker_bypass included
            // from day one per the TASK-343 lesson, even though no worker job touches these
            // tables today.
            migrationBuilder.Sql(@"
                ALTER TABLE marketplace_order_receipts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE marketplace_order_receipts FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON marketplace_order_receipts
                  USING (""ClientTenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
                  WITH CHECK (""ClientTenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

                CREATE POLICY supplier_read ON marketplace_order_receipts
                  FOR SELECT
                  USING (""SupplierTenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

                CREATE POLICY provider_bypass ON marketplace_order_receipts
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));

                CREATE POLICY worker_bypass ON marketplace_order_receipts
                  USING (current_setting('app.role', true) = 'worker');
            ");

            // ── RLS: marketplace_order_receipt_items ────────────────────────
            // Identical shape to marketplace_order_receipts above, same column names (items
            // carry denormalised ClientTenantId/SupplierTenantId copies of the parent receipt).
            migrationBuilder.Sql(@"
                ALTER TABLE marketplace_order_receipt_items ENABLE ROW LEVEL SECURITY;
                ALTER TABLE marketplace_order_receipt_items FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON marketplace_order_receipt_items
                  USING (""ClientTenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
                  WITH CHECK (""ClientTenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

                CREATE POLICY supplier_read ON marketplace_order_receipt_items
                  FOR SELECT
                  USING (""SupplierTenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);

                CREATE POLICY provider_bypass ON marketplace_order_receipt_items
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));

                CREATE POLICY worker_bypass ON marketplace_order_receipt_items
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS worker_bypass ON marketplace_order_receipt_items;
                DROP POLICY IF EXISTS provider_bypass ON marketplace_order_receipt_items;
                DROP POLICY IF EXISTS supplier_read ON marketplace_order_receipt_items;
                DROP POLICY IF EXISTS tenant_isolation ON marketplace_order_receipt_items;
                ALTER TABLE marketplace_order_receipt_items DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS worker_bypass ON marketplace_order_receipts;
                DROP POLICY IF EXISTS provider_bypass ON marketplace_order_receipts;
                DROP POLICY IF EXISTS supplier_read ON marketplace_order_receipts;
                DROP POLICY IF EXISTS tenant_isolation ON marketplace_order_receipts;
                ALTER TABLE marketplace_order_receipts DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_marketplace_orders_locations_DestinationStoreId",
                table: "marketplace_orders");

            migrationBuilder.DropTable(
                name: "marketplace_order_receipt_items");

            migrationBuilder.DropTable(
                name: "marketplace_order_receipts");

            migrationBuilder.DropIndex(
                name: "IX_marketplace_orders_DestinationStoreId",
                table: "marketplace_orders");

            migrationBuilder.DropColumn(
                name: "DestinationStoreId",
                table: "marketplace_orders");
        }
    }
}
