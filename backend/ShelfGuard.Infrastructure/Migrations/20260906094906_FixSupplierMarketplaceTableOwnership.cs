using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Same class of bug as <see cref="FixLoyaltyTableGrants"/> (20260726154747), for the
    /// supplier-portal expansion tables.
    ///
    /// This codebase has no bootstrap script and no <c>ALTER DEFAULT PRIVILEGES</c> — a table is
    /// reachable by the runtime app role <em>only</em> if that role OWNS it. Normally every new
    /// table inherits correct ownership because the migration that creates it runs through the
    /// app's own (already-owning) connection. But the Phase 2 / 3 / 8 migrations
    /// (<c>AddSupplierInventory</c>, <c>AddMarketplaceOrderItemBatches</c>,
    /// <c>AddSupplierEmployeeReviews</c>) were applied to dev by connecting directly as the
    /// bootstrap superuser (<c>crm</c>) via <c>psql</c>, so their 6 tables ended up owned by
    /// <c>crm</c> with ZERO privileges for the app role — confirmed live: the app role gets
    /// Postgres 42501 "permission denied for table supplier_stock_receipt_items" on any
    /// SELECT/INSERT, which is exactly the 500 the supplier inventory / receiving screens hit.
    ///
    /// Fix: transfer ownership of exactly these 6 tables to whichever role currently owns
    /// <c>tenants</c> — this environment's established app role (<c>shelfguard_app_dev</c> in dev,
    /// production's own). No schema, grant, or RLS-policy change.
    ///
    /// Operational note (mirrors <see cref="FixLoyaltyTableGrants"/>): <c>ALTER TABLE ... OWNER TO</c>
    /// needs the executing role to be a superuser or the table's current owner. The app's boot-time
    /// <c>Database.MigrateAsync()</c> runs as the restricted app role, which is neither for these 6
    /// tables — so the statements are wrapped in an <c>insufficient_privilege</c> handler: on an
    /// environment where the boot role cannot re-own them the migration records as applied and
    /// emits a WARNING with the exact command to run once as a superuser, rather than aborting
    /// startup with "must be owner of relation". Dev was fixed directly as <c>crm</c>.
    /// </summary>
    public partial class FixSupplierMarketplaceTableOwnership : Migration
    {
        private static readonly string[] Tables =
        {
            "supplier_stock",
            "supplier_stock_movements",
            "supplier_stock_receipts",
            "supplier_stock_receipt_items",
            "marketplace_order_item_batches",
            "supplier_employee_reviews",
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var alters = string.Join("\n                    ",
                System.Array.ConvertAll(Tables,
                    t => $"EXECUTE format('ALTER TABLE IF EXISTS {t} OWNER TO %I', app_owner);"));

            migrationBuilder.Sql($@"
                DO $$
                DECLARE
                  app_owner text;
                BEGIN
                  SELECT tableowner INTO app_owner
                  FROM pg_tables
                  WHERE schemaname = 'public' AND tablename = 'tenants';

                  IF app_owner IS NULL THEN
                    RAISE EXCEPTION
                      'FixSupplierMarketplaceTableOwnership: could not resolve owner of table ""tenants"" — aborting rather than guessing a role name';
                  END IF;

                  BEGIN
                    {alters}
                    RAISE NOTICE 'FixSupplierMarketplaceTableOwnership: 6 tables re-owned to %', app_owner;
                  EXCEPTION WHEN insufficient_privilege THEN
                    RAISE WARNING 'FixSupplierMarketplaceTableOwnership: migration role lacks privilege to re-own the 6 supplier/marketplace tables (%). Run ONCE as a superuser: ALTER TABLE supplier_stock, supplier_stock_movements, supplier_stock_receipts, supplier_stock_receipt_items, marketplace_order_item_batches, supplier_employee_reviews OWNER TO %I;', SQLERRM, app_owner;
                  END;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op: reverting would hand these tables' ownership back to the
            // migration superuser, silently reintroducing the exact 42501 permission-denied bug
            // this migration fixes. Same precedent as FixLoyaltyTableGrants (20260726154747) and
            // FixAllRlsPoliciesNullIfEmptyString (20260629010000).
        }
    }
}
