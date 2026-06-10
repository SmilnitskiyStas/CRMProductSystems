using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixRlsAndForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── RLS: notification_settings ────────────────────────────────────
            // No direct TenantId — isolate via owning user's TenantId.
            // Provider users (TenantId IS NULL) are accessible by provider_bypass.
            migrationBuilder.Sql(@"
                ALTER TABLE notification_settings ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON notification_settings
                  USING (EXISTS (
                    SELECT 1 FROM users u
                    WHERE u.""Id"" = ""UserId""
                      AND (u.""TenantId"" = current_setting('app.tenant_id', true)::uuid
                           OR u.""TenantId"" IS NULL)
                  ));

                CREATE POLICY provider_bypass ON notification_settings
                  USING (current_setting('app.role', true) = 'provider');
            ");

            // ── FK: stock_movements → catalog_products ────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE stock_movements
                  ADD CONSTRAINT ""FK_stock_movements_catalog_products_ProductId""
                  FOREIGN KEY (""ProductId"")
                  REFERENCES catalog_products(""Id"")
                  ON DELETE RESTRICT;
            ");

            // ── FK: stock_movements → stores (FromStoreId) ───────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE stock_movements
                  ADD CONSTRAINT ""FK_stock_movements_stores_FromStoreId""
                  FOREIGN KEY (""FromStoreId"")
                  REFERENCES stores(""Id"")
                  ON DELETE RESTRICT;
            ");

            // ── FK: stock_movements → stores (ToStoreId) ─────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE stock_movements
                  ADD CONSTRAINT ""FK_stock_movements_stores_ToStoreId""
                  FOREIGN KEY (""ToStoreId"")
                  REFERENCES stores(""Id"")
                  ON DELETE RESTRICT;
            ");

            // ── FK: write_offs → stores ───────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE write_offs
                  ADD CONSTRAINT ""FK_write_offs_stores_StoreId""
                  FOREIGN KEY (""StoreId"")
                  REFERENCES stores(""Id"")
                  ON DELETE RESTRICT;
            ");

            // ── FK: discounts → catalog_products ──────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE discounts
                  ADD CONSTRAINT ""FK_discounts_catalog_products_ProductId""
                  FOREIGN KEY (""ProductId"")
                  REFERENCES catalog_products(""Id"")
                  ON DELETE RESTRICT;
            ");

            // ── FK: discounts → stores ────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE discounts
                  ADD CONSTRAINT ""FK_discounts_stores_StoreId""
                  FOREIGN KEY (""StoreId"")
                  REFERENCES stores(""Id"")
                  ON DELETE RESTRICT;
            ");

            // ── FK: discounts → product_stock (nullable) ──────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE discounts
                  ADD CONSTRAINT ""FK_discounts_product_stock_ProductStockId""
                  FOREIGN KEY (""ProductStockId"")
                  REFERENCES product_stock(""Id"")
                  ON DELETE SET NULL;
            ");

            // ── Performance indexes for stock_movements queries ───────────────
            // Supports GET /movements with typical filter combinations
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS idx_movements_tenant_type
                  ON stock_movements(""TenantId"", ""MovementType"");

                CREATE INDEX IF NOT EXISTS idx_movements_tenant_store
                  ON stock_movements(""TenantId"", ""FromStoreId"", ""ToStoreId"");

                CREATE INDEX IF NOT EXISTS idx_movements_product
                  ON stock_movements(""TenantId"", ""ProductId"");

                CREATE INDEX IF NOT EXISTS idx_movements_created_at
                  ON stock_movements(""TenantId"", ""CreatedAt"" DESC);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop indexes
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS idx_movements_tenant_type;
                DROP INDEX IF EXISTS idx_movements_tenant_store;
                DROP INDEX IF EXISTS idx_movements_product;
                DROP INDEX IF EXISTS idx_movements_created_at;
            ");

            // Drop FKs
            migrationBuilder.Sql(@"
                ALTER TABLE discounts DROP CONSTRAINT IF EXISTS ""FK_discounts_product_stock_ProductStockId"";
                ALTER TABLE discounts DROP CONSTRAINT IF EXISTS ""FK_discounts_stores_StoreId"";
                ALTER TABLE discounts DROP CONSTRAINT IF EXISTS ""FK_discounts_catalog_products_ProductId"";
                ALTER TABLE write_offs DROP CONSTRAINT IF EXISTS ""FK_write_offs_stores_StoreId"";
                ALTER TABLE stock_movements DROP CONSTRAINT IF EXISTS ""FK_stock_movements_stores_ToStoreId"";
                ALTER TABLE stock_movements DROP CONSTRAINT IF EXISTS ""FK_stock_movements_stores_FromStoreId"";
                ALTER TABLE stock_movements DROP CONSTRAINT IF EXISTS ""FK_stock_movements_catalog_products_ProductId"";
            ");

            // Drop RLS
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS provider_bypass ON notification_settings;
                DROP POLICY IF EXISTS tenant_isolation ON notification_settings;
                ALTER TABLE notification_settings DISABLE ROW LEVEL SECURITY;
            ");
        }
    }
}
