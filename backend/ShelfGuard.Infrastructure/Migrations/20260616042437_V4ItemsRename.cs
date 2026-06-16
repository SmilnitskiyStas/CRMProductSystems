using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V4ItemsRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ai_order_suggestion_items_catalog_products_ProductId",
                table: "ai_order_suggestion_items");

            migrationBuilder.DropForeignKey(
                name: "FK_catalog_products_categories_CategoryId",
                table: "catalog_products");

            migrationBuilder.DropForeignKey(
                name: "FK_catalog_products_product_segments_SegmentId",
                table: "catalog_products");

            migrationBuilder.DropForeignKey(
                name: "FK_catalog_products_suppliers_DefaultSupplierId",
                table: "catalog_products");

            migrationBuilder.DropForeignKey(
                name: "FK_catalog_products_tenants_TenantId",
                table: "catalog_products");

            migrationBuilder.DropForeignKey(
                name: "FK_daily_sales_catalog_products_ProductId",
                table: "daily_sales");

            migrationBuilder.DropForeignKey(
                name: "FK_pos_transaction_items_catalog_products_ProductId",
                table: "pos_transaction_items");

            migrationBuilder.DropForeignKey(
                name: "FK_product_adu_catalog_products_ProductId",
                table: "product_adu");

            migrationBuilder.DropForeignKey(
                name: "FK_product_buffer_catalog_products_ProductId",
                table: "product_buffer");

            migrationBuilder.DropForeignKey(
                name: "FK_product_stock_catalog_products_ProductId",
                table: "product_stock");

            migrationBuilder.DropForeignKey(
                name: "FK_product_supplier_settings_catalog_products_ProductId",
                table: "product_supplier_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_promo_cannibalization_catalog_products_AffectedProductId",
                table: "promo_cannibalization");

            migrationBuilder.DropForeignKey(
                name: "FK_discounts_catalog_products_ProductId",
                table: "discounts");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_catalog_products_ProductId",
                table: "stock_movements");

            migrationBuilder.DropPrimaryKey(
                name: "PK_catalog_products",
                table: "catalog_products");

            migrationBuilder.RenameTable(
                name: "catalog_products",
                newName: "items");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_products_TenantId",
                table: "items",
                newName: "IX_items_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_products_SegmentId",
                table: "items",
                newName: "IX_items_SegmentId");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_products_DefaultSupplierId",
                table: "items",
                newName: "IX_items_DefaultSupplierId");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_products_CategoryId",
                table: "items",
                newName: "IX_items_CategoryId");

            migrationBuilder.AddColumn<string>(
                name: "ItemType",
                table: "items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "product");

            migrationBuilder.AddPrimaryKey(
                name: "PK_items",
                table: "items",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ai_order_suggestion_items_items_ProductId",
                table: "ai_order_suggestion_items",
                column: "ProductId",
                principalTable: "items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_daily_sales_items_ProductId",
                table: "daily_sales",
                column: "ProductId",
                principalTable: "items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_items_categories_CategoryId",
                table: "items",
                column: "CategoryId",
                principalTable: "categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_items_product_segments_SegmentId",
                table: "items",
                column: "SegmentId",
                principalTable: "product_segments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_items_suppliers_DefaultSupplierId",
                table: "items",
                column: "DefaultSupplierId",
                principalTable: "suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_items_tenants_TenantId",
                table: "items",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pos_transaction_items_items_ProductId",
                table: "pos_transaction_items",
                column: "ProductId",
                principalTable: "items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_product_adu_items_ProductId",
                table: "product_adu",
                column: "ProductId",
                principalTable: "items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_product_buffer_items_ProductId",
                table: "product_buffer",
                column: "ProductId",
                principalTable: "items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_product_stock_items_ProductId",
                table: "product_stock",
                column: "ProductId",
                principalTable: "items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_product_supplier_settings_items_ProductId",
                table: "product_supplier_settings",
                column: "ProductId",
                principalTable: "items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_promo_cannibalization_items_AffectedProductId",
                table: "promo_cannibalization",
                column: "AffectedProductId",
                principalTable: "items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_discounts_items_ProductId",
                table: "discounts",
                column: "ProductId",
                principalTable: "items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_items_ProductId",
                table: "stock_movements",
                column: "ProductId",
                principalTable: "items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ai_order_suggestion_items_items_ProductId",
                table: "ai_order_suggestion_items");

            migrationBuilder.DropForeignKey(
                name: "FK_daily_sales_items_ProductId",
                table: "daily_sales");

            migrationBuilder.DropForeignKey(
                name: "FK_items_categories_CategoryId",
                table: "items");

            migrationBuilder.DropForeignKey(
                name: "FK_items_product_segments_SegmentId",
                table: "items");

            migrationBuilder.DropForeignKey(
                name: "FK_items_suppliers_DefaultSupplierId",
                table: "items");

            migrationBuilder.DropForeignKey(
                name: "FK_items_tenants_TenantId",
                table: "items");

            migrationBuilder.DropForeignKey(
                name: "FK_pos_transaction_items_items_ProductId",
                table: "pos_transaction_items");

            migrationBuilder.DropForeignKey(
                name: "FK_product_adu_items_ProductId",
                table: "product_adu");

            migrationBuilder.DropForeignKey(
                name: "FK_product_buffer_items_ProductId",
                table: "product_buffer");

            migrationBuilder.DropForeignKey(
                name: "FK_product_stock_items_ProductId",
                table: "product_stock");

            migrationBuilder.DropForeignKey(
                name: "FK_product_supplier_settings_items_ProductId",
                table: "product_supplier_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_promo_cannibalization_items_AffectedProductId",
                table: "promo_cannibalization");

            migrationBuilder.DropForeignKey(
                name: "FK_discounts_items_ProductId",
                table: "discounts");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_items_ProductId",
                table: "stock_movements");

            migrationBuilder.DropPrimaryKey(
                name: "PK_items",
                table: "items");

            migrationBuilder.DropColumn(
                name: "ItemType",
                table: "items");

            migrationBuilder.RenameTable(
                name: "items",
                newName: "catalog_products");

            migrationBuilder.RenameIndex(
                name: "IX_items_TenantId",
                table: "catalog_products",
                newName: "IX_catalog_products_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_items_SegmentId",
                table: "catalog_products",
                newName: "IX_catalog_products_SegmentId");

            migrationBuilder.RenameIndex(
                name: "IX_items_DefaultSupplierId",
                table: "catalog_products",
                newName: "IX_catalog_products_DefaultSupplierId");

            migrationBuilder.RenameIndex(
                name: "IX_items_CategoryId",
                table: "catalog_products",
                newName: "IX_catalog_products_CategoryId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_catalog_products",
                table: "catalog_products",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ai_order_suggestion_items_catalog_products_ProductId",
                table: "ai_order_suggestion_items",
                column: "ProductId",
                principalTable: "catalog_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_products_categories_CategoryId",
                table: "catalog_products",
                column: "CategoryId",
                principalTable: "categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_products_product_segments_SegmentId",
                table: "catalog_products",
                column: "SegmentId",
                principalTable: "product_segments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_products_suppliers_DefaultSupplierId",
                table: "catalog_products",
                column: "DefaultSupplierId",
                principalTable: "suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_products_tenants_TenantId",
                table: "catalog_products",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_daily_sales_catalog_products_ProductId",
                table: "daily_sales",
                column: "ProductId",
                principalTable: "catalog_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_pos_transaction_items_catalog_products_ProductId",
                table: "pos_transaction_items",
                column: "ProductId",
                principalTable: "catalog_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_product_adu_catalog_products_ProductId",
                table: "product_adu",
                column: "ProductId",
                principalTable: "catalog_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_product_buffer_catalog_products_ProductId",
                table: "product_buffer",
                column: "ProductId",
                principalTable: "catalog_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_product_stock_catalog_products_ProductId",
                table: "product_stock",
                column: "ProductId",
                principalTable: "catalog_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_product_supplier_settings_catalog_products_ProductId",
                table: "product_supplier_settings",
                column: "ProductId",
                principalTable: "catalog_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_promo_cannibalization_catalog_products_AffectedProductId",
                table: "promo_cannibalization",
                column: "AffectedProductId",
                principalTable: "catalog_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_discounts_catalog_products_ProductId",
                table: "discounts",
                column: "ProductId",
                principalTable: "catalog_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_catalog_products_ProductId",
                table: "stock_movements",
                column: "ProductId",
                principalTable: "catalog_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
