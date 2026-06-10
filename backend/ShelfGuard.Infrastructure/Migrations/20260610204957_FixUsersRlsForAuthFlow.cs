using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixUsersRlsForAuthFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fix RLS on users to allow login lookups when no tenant context is set.
            // TenantConnectionInterceptor now RESETs app.tenant_id for unauthenticated
            // requests (login endpoint), so current_setting returns NULL.
            // Without this change, tenant users (TenantId != NULL) are invisible during
            // login because the interceptor's null-UUID from a pooled connection blocks them.
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_isolation ON users;
                CREATE POLICY tenant_isolation ON users
                  USING (current_setting('app.tenant_id', true) IS NULL
                         OR ""TenantId"" = current_setting('app.tenant_id', true)::uuid
                         OR ""TenantId"" IS NULL);
            ");

            migrationBuilder.CreateIndex(
                name: "IX_write_offs_StoreId",
                table: "write_offs",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_write_off_items_ProductId",
                table: "write_off_items",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_write_off_items_ProductStockId",
                table: "write_off_items",
                column: "ProductStockId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_FromStoreId",
                table: "stock_transfers",
                column: "FromStoreId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_ToStoreId",
                table: "stock_transfers",
                column: "ToStoreId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfer_items_ProductId",
                table: "stock_transfer_items",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_receipts_DestinationStoreId",
                table: "stock_receipts",
                column: "DestinationStoreId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_receipts_SupplierId",
                table: "stock_receipts",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_receipt_items_ProductId",
                table: "stock_receipt_items",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_stock_receipt_items_catalog_products_ProductId",
                table: "stock_receipt_items",
                column: "ProductId",
                principalTable: "catalog_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_receipts_stores_DestinationStoreId",
                table: "stock_receipts",
                column: "DestinationStoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_receipts_suppliers_SupplierId",
                table: "stock_receipts",
                column: "SupplierId",
                principalTable: "suppliers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_stock_transfer_items_catalog_products_ProductId",
                table: "stock_transfer_items",
                column: "ProductId",
                principalTable: "catalog_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_transfers_stores_FromStoreId",
                table: "stock_transfers",
                column: "FromStoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_transfers_stores_ToStoreId",
                table: "stock_transfers",
                column: "ToStoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_write_off_items_catalog_products_ProductId",
                table: "write_off_items",
                column: "ProductId",
                principalTable: "catalog_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_write_off_items_product_stock_ProductStockId",
                table: "write_off_items",
                column: "ProductStockId",
                principalTable: "product_stock",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_write_offs_stores_StoreId",
                table: "write_offs",
                column: "StoreId",
                principalTable: "stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore original policy (without the IS NULL auth bypass).
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_isolation ON users;
                CREATE POLICY tenant_isolation ON users
                  USING (""TenantId"" = current_setting('app.tenant_id', true)::uuid
                         OR ""TenantId"" IS NULL);
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_receipt_items_catalog_products_ProductId",
                table: "stock_receipt_items");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_receipts_stores_DestinationStoreId",
                table: "stock_receipts");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_receipts_suppliers_SupplierId",
                table: "stock_receipts");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_transfer_items_catalog_products_ProductId",
                table: "stock_transfer_items");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_transfers_stores_FromStoreId",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_transfers_stores_ToStoreId",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_write_off_items_catalog_products_ProductId",
                table: "write_off_items");

            migrationBuilder.DropForeignKey(
                name: "FK_write_off_items_product_stock_ProductStockId",
                table: "write_off_items");

            migrationBuilder.DropForeignKey(
                name: "FK_write_offs_stores_StoreId",
                table: "write_offs");

            migrationBuilder.DropIndex(
                name: "IX_write_offs_StoreId",
                table: "write_offs");

            migrationBuilder.DropIndex(
                name: "IX_write_off_items_ProductId",
                table: "write_off_items");

            migrationBuilder.DropIndex(
                name: "IX_write_off_items_ProductStockId",
                table: "write_off_items");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfers_FromStoreId",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfers_ToStoreId",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfer_items_ProductId",
                table: "stock_transfer_items");

            migrationBuilder.DropIndex(
                name: "IX_stock_receipts_DestinationStoreId",
                table: "stock_receipts");

            migrationBuilder.DropIndex(
                name: "IX_stock_receipts_SupplierId",
                table: "stock_receipts");

            migrationBuilder.DropIndex(
                name: "IX_stock_receipt_items_ProductId",
                table: "stock_receipt_items");
        }
    }
}
