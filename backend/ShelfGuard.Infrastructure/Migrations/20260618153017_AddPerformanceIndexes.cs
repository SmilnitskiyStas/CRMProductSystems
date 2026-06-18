using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pos_transactions_TenantId_LocationId_CreatedAt",
                table: "pos_transactions");

            migrationBuilder.DropIndex(
                name: "IX_pos_transaction_items_TransactionId",
                table: "pos_transaction_items");

            migrationBuilder.DropIndex(
                name: "IX_items_TenantId",
                table: "items");

            migrationBuilder.DropIndex(
                name: "IX_discounts_TenantId",
                table: "discounts");

            migrationBuilder.DropIndex(
                name: "IX_categories_TenantId",
                table: "categories");

            migrationBuilder.CreateIndex(
                name: "idx_write_offs_tenant_store_status",
                table: "write_offs",
                columns: new[] { "TenantId", "LocationId", "Status", "CreatedAt" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "idx_stock_fefo_active",
                table: "product_stock",
                columns: new[] { "TenantId", "LocationId", "ProductId", "ExpiryDate" },
                filter: "\"Quantity\" > 0");

            migrationBuilder.CreateIndex(
                name: "idx_stock_tenant_status_addedat",
                table: "product_stock",
                columns: new[] { "TenantId", "Status", "AddedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "idx_stock_tenant_store_covering",
                table: "product_stock",
                columns: new[] { "TenantId", "LocationId" })
                .Annotation("Npgsql:IndexInclude", new[] { "Status", "Quantity" });

            migrationBuilder.CreateIndex(
                name: "idx_stock_tenant_zone_active",
                table: "product_stock",
                columns: new[] { "TenantId", "ZoneId" },
                filter: "\"Quantity\" > 0");

            migrationBuilder.CreateIndex(
                name: "idx_pos_transactions_excl_failed",
                table: "pos_transactions",
                columns: new[] { "TenantId", "LocationId", "CreatedAt" },
                descending: new[] { false, false, true },
                filter: "\"Status\" <> 'fiscalization_failed'");

            migrationBuilder.CreateIndex(
                name: "idx_pos_transaction_items_txn_covering",
                table: "pos_transaction_items",
                column: "TransactionId")
                .Annotation("Npgsql:IndexInclude", new[] { "ProductId", "PriceFinal", "Quantity" });

            migrationBuilder.CreateIndex(
                name: "idx_notification_queue_tenant_status",
                table: "notification_queue",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_items_barcode_active",
                table: "items",
                column: "Barcode",
                filter: "\"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "idx_items_tenant_category_segment_active",
                table: "items",
                columns: new[] { "TenantId", "CategoryId", "SegmentId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "idx_discounts_active",
                table: "discounts",
                columns: new[] { "TenantId", "Status", "ValidFrom", "ValidUntil" },
                filter: "\"Status\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "idx_daily_sales_tenant_store_product_date",
                table: "daily_sales",
                columns: new[] { "TenantId", "LocationId", "ProductId", "Date" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "idx_categories_tenant_parent_active",
                table: "categories",
                columns: new[] { "TenantId", "ParentId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_write_offs_tenant_store_status",
                table: "write_offs");

            migrationBuilder.DropIndex(
                name: "idx_stock_fefo_active",
                table: "product_stock");

            migrationBuilder.DropIndex(
                name: "idx_stock_tenant_status_addedat",
                table: "product_stock");

            migrationBuilder.DropIndex(
                name: "idx_stock_tenant_store_covering",
                table: "product_stock");

            migrationBuilder.DropIndex(
                name: "idx_stock_tenant_zone_active",
                table: "product_stock");

            migrationBuilder.DropIndex(
                name: "idx_pos_transactions_excl_failed",
                table: "pos_transactions");

            migrationBuilder.DropIndex(
                name: "idx_pos_transaction_items_txn_covering",
                table: "pos_transaction_items");

            migrationBuilder.DropIndex(
                name: "idx_notification_queue_tenant_status",
                table: "notification_queue");

            migrationBuilder.DropIndex(
                name: "idx_items_barcode_active",
                table: "items");

            migrationBuilder.DropIndex(
                name: "idx_items_tenant_category_segment_active",
                table: "items");

            migrationBuilder.DropIndex(
                name: "idx_discounts_active",
                table: "discounts");

            migrationBuilder.DropIndex(
                name: "idx_daily_sales_tenant_store_product_date",
                table: "daily_sales");

            migrationBuilder.DropIndex(
                name: "idx_categories_tenant_parent_active",
                table: "categories");

            migrationBuilder.CreateIndex(
                name: "IX_pos_transactions_TenantId_LocationId_CreatedAt",
                table: "pos_transactions",
                columns: new[] { "TenantId", "LocationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pos_transaction_items_TransactionId",
                table: "pos_transaction_items",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_items_TenantId",
                table: "items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_discounts_TenantId",
                table: "discounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_categories_TenantId",
                table: "categories",
                column: "TenantId");
        }
    }
}
