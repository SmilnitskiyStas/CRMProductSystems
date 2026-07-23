using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// TASK-393 Stage 3 (Feature 2 — store-scoped data visibility, enforcement cutover).
    ///
    /// ⚠️⚠️ DO NOT APPLY TO PRODUCTION until Stage 2's manual backfill is complete — see
    /// .claude/docs/store-scope-rollout-checklist.md. Every network_manager/store_manager/
    /// merchandiser/storekeeper/cashier/staff user with ZERO rows in user_locations will see
    /// EMPTY results (product_stock, sales, POS, everything this migration touches) the instant
    /// this RESTRICTIVE policy activates — not degraded, not partial: a full functional outage
    /// for that user across every scoped table, immediately, no gradual rollout possible once
    /// applied. This is fail-closed by design (confirmed acceptable end-state with product
    /// owner), but only AFTER coverage is verified at 100% — never before.
    ///
    /// Adds a RESTRICTIVE `store_scope` policy on the 9 tables that carry per-location
    /// operational data. RESTRICTIVE (not PERMISSIVE) is required: PERMISSIVE policies OR
    /// together, so a PERMISSIVE store_scope would be silently defeated by the existing
    /// permissive tenant_isolation policy (any tenant match would satisfy the OR regardless of
    /// location). RESTRICTIVE ANDs on top of the permissive set instead — the correct semantics
    /// for "narrow what tenant_isolation already allowed", not "grant an additional way in".
    ///
    /// Bypass roles: 'provider', 'provider_admin', 'worker', 'enterprise_admin'.
    /// 'provider'/'worker'/'enterprise_admin' come directly from the brief agreed with
    /// project-architect. 'provider_admin' is an addition made here, not in the original
    /// brief text — flagging explicitly: ExpandProviderBypassToProviderAdmin
    /// (20260714150000) already gave provider_admin full parity with provider on all 9 of
    /// these tables via the existing permissive provider_bypass policy (confirmed by reading
    /// that migration's table list — all 9 are in it). Omitting provider_admin from this
    /// RESTRICTIVE policy's bypass condition would silently revoke that already-established,
    /// already-audited access the moment this migration applied — a real regression, not a
    /// re-litigation of the network_manager-and-below access model this task is actually
    /// about. Included here to preserve existing behavior; flagged for project-architect/
    /// product-owner to override if provider_admin's blanket visibility on these specific 9
    /// tables was actually meant to narrow too (nothing read so far suggests that was intended).
    /// 'provider_agent' is deliberately NOT included — it never had bypass on these 9 tables
    /// (only support_tickets/ticket_comments/chat_sessions), so no regression risk there.
    ///
    /// Column names verified against the live EF model / AppDbContext.cs fluent config for each
    /// table individually (not assumed) — several diverge from the brief's literal "StoreId"
    /// shorthand and from its one-sided/two-sided table classification:
    ///   - product_stock, daily_sales, pos_shifts, pos_transactions, write_offs, discounts:
    ///     one-sided, physical column "LocationId" (C# property StoreId, HasColumnName mapping).
    ///   - stock_receipts: one-sided, physical column "DestinationLocationId" (C# property
    ///     DestinationStoreId) — the brief listed this table as two-sided ("FromStoreId/
    ///     ToStoreId чи подібні дві колонки"), but the actual entity
    ///     (ShelfGuard.Domain/Entities/StockReceipt.cs) has only ONE store column; a receipt
    ///     comes from a supplier, not another store, so there is no second side to OR against.
    ///   - stock_transfers: two-sided, physical columns "FromLocationId"/"ToLocationId" (C#
    ///     FromStoreId/ToStoreId), both NOT NULL — matches the brief's classification.
    ///   - stock_movements: two-sided, physical columns "FromLocationId"/"ToLocationId" (C#
    ///     FromStoreId/ToStoreId), both NULLABLE — the brief listed this table as one-sided,
    ///     but the actual entity (ShelfGuard.Domain/Entities/StockMovement.cs) has both a
    ///     Guid? FromStoreId and a Guid? ToStoreId (e.g. a receipt-origin movement only
    ///     populates ToStoreId, a write-off-origin movement only populates FromStoreId). A
    ///     plain equality against a NULL column is simply unmatched (SQL NULL semantics, no
    ///     special-case needed) — the OR-both-columns shape handles this correctly without
    ///     extra guards. In the rare case neither column is populated on a row, only the
    ///     bypass roles can see it — a deliberate fail-closed default, not a bug.
    ///
    /// No child tables (stock_receipt_items, stock_transfer_items, write_off_items,
    /// pos_transaction_items) need their own store_scope policy — Postgres re-applies a
    /// referenced table's RLS policies (RESTRICTIVE included) inside any EXISTS/subquery/join
    /// that reads it, so those child tables' existing tenant_isolation EXISTS-into-parent
    /// policies automatically inherit this narrowing for free, the same way
    /// supplier_chat_messages already inherits its tenant scoping from supplier_chat_sessions.
    /// </summary>
    public partial class AddLocationStoreScopeRlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── One-sided tables: single store/location column ─────────────────────
            // Shared shape: bypass roles OR an EXISTS into user_locations for the exact
            // location this row belongs to. NULLIF guard on app.user_id mirrors the
            // established app.tenant_id pattern — current_setting returns '' (not NULL) after
            // RESET / on a raw session that never set it, and casting '' to uuid throws.
            migrationBuilder.Sql(@"
                CREATE POLICY store_scope ON product_stock AS RESTRICTIVE
                  USING (
                    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
                    OR EXISTS (
                         SELECT 1 FROM user_locations ul
                         WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                           AND ul.""LocationId"" = product_stock.""LocationId""
                       )
                  );
            ");

            migrationBuilder.Sql(@"
                CREATE POLICY store_scope ON daily_sales AS RESTRICTIVE
                  USING (
                    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
                    OR EXISTS (
                         SELECT 1 FROM user_locations ul
                         WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                           AND ul.""LocationId"" = daily_sales.""LocationId""
                       )
                  );
            ");

            migrationBuilder.Sql(@"
                CREATE POLICY store_scope ON pos_shifts AS RESTRICTIVE
                  USING (
                    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
                    OR EXISTS (
                         SELECT 1 FROM user_locations ul
                         WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                           AND ul.""LocationId"" = pos_shifts.""LocationId""
                       )
                  );
            ");

            migrationBuilder.Sql(@"
                CREATE POLICY store_scope ON pos_transactions AS RESTRICTIVE
                  USING (
                    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
                    OR EXISTS (
                         SELECT 1 FROM user_locations ul
                         WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                           AND ul.""LocationId"" = pos_transactions.""LocationId""
                       )
                  );
            ");

            migrationBuilder.Sql(@"
                CREATE POLICY store_scope ON write_offs AS RESTRICTIVE
                  USING (
                    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
                    OR EXISTS (
                         SELECT 1 FROM user_locations ul
                         WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                           AND ul.""LocationId"" = write_offs.""LocationId""
                       )
                  );
            ");

            migrationBuilder.Sql(@"
                CREATE POLICY store_scope ON discounts AS RESTRICTIVE
                  USING (
                    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
                    OR EXISTS (
                         SELECT 1 FROM user_locations ul
                         WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                           AND ul.""LocationId"" = discounts.""LocationId""
                       )
                  );
            ");

            // stock_receipts: one-sided despite the brief's "two-sided" guess — see class
            // remarks. Single column DestinationLocationId (a receipt's origin is a supplier,
            // not another store).
            migrationBuilder.Sql(@"
                CREATE POLICY store_scope ON stock_receipts AS RESTRICTIVE
                  USING (
                    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
                    OR EXISTS (
                         SELECT 1 FROM user_locations ul
                         WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                           AND ul.""LocationId"" = stock_receipts.""DestinationLocationId""
                       )
                  );
            ");

            // ── Two-sided tables: OR across both store/location columns ────────────
            // stock_movements: FromLocationId/ToLocationId are both NULLABLE — see class
            // remarks for why a plain OR-equality needs no extra NULL handling.
            migrationBuilder.Sql(@"
                CREATE POLICY store_scope ON stock_movements AS RESTRICTIVE
                  USING (
                    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
                    OR EXISTS (
                         SELECT 1 FROM user_locations ul
                         WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                           AND (ul.""LocationId"" = stock_movements.""FromLocationId""
                                OR ul.""LocationId"" = stock_movements.""ToLocationId"")
                       )
                  );
            ");

            // stock_transfers: FromLocationId/ToLocationId both NOT NULL. Same OR shape as
            // supplier_chat_sessions' SupplierTenantId/ClientTenantId pattern (20260706110628).
            migrationBuilder.Sql(@"
                CREATE POLICY store_scope ON stock_transfers AS RESTRICTIVE
                  USING (
                    current_setting('app.role', true) IN ('provider', 'provider_admin', 'worker', 'enterprise_admin')
                    OR EXISTS (
                         SELECT 1 FROM user_locations ul
                         WHERE ul.""UserId"" = NULLIF(current_setting('app.user_id', true), '')::uuid
                           AND (ul.""LocationId"" = stock_transfers.""FromLocationId""
                                OR ul.""LocationId"" = stock_transfers.""ToLocationId"")
                       )
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS store_scope ON product_stock;
                DROP POLICY IF EXISTS store_scope ON daily_sales;
                DROP POLICY IF EXISTS store_scope ON pos_shifts;
                DROP POLICY IF EXISTS store_scope ON pos_transactions;
                DROP POLICY IF EXISTS store_scope ON write_offs;
                DROP POLICY IF EXISTS store_scope ON discounts;
                DROP POLICY IF EXISTS store_scope ON stock_receipts;
                DROP POLICY IF EXISTS store_scope ON stock_movements;
                DROP POLICY IF EXISTS store_scope ON stock_transfers;
            ");
        }
    }
}
