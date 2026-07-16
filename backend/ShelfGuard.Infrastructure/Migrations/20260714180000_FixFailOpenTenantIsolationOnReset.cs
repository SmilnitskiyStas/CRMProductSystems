using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260714180000_FixFailOpenTenantIsolationOnReset")]
    public partial class FixFailOpenTenantIsolationOnReset : Migration
    {
        // P0 found and independently reproduced 2026-07-14 (Block 2 pre-launch audit,
        // continuation of TASK-352): the 2026-06-29 bulk NULLIF-guard fixes
        // (20260629000000_FixUsersRlsNullIfEmptyString, 20260629010000_
        // FixAllRlsPoliciesNullIfEmptyString) did more than add a NULLIF guard — they
        // prefixed every affected tenant_isolation policy with
        // `NULLIF(current_setting('app.tenant_id', true), '') IS NULL OR ...`. That prefix is
        // FAIL-OPEN: when app.tenant_id is unset (the RESET state TenantConnectionInterceptor
        // uses for every unauthenticated connection, and the state ANY raw non-superuser
        // Postgres session is in before it explicitly sets app.tenant_id), the policy evaluates
        // to TRUE for every row, returning ALL tenants' data instead of zero rows. This is the
        // opposite of what `.claude/agents/database-engineer.md`'s canonical pattern specifies
        // (`tenant_id = NULLIF(current_setting(...), '')::uuid` — no IS-NULL-OR branch; a NULL
        // comparison is simply falsy, so RESET correctly yields zero rows, not all rows).
        // Reproduced live: a real NOSUPERUSER/NOBYPASSRLS role, RESET app.tenant_id;
        // RESET app.role;, then `SELECT count(*) FROM product_stock` returned every row instead
        // of zero. My own 20260714100000_FixMissingRlsGuardsAndProviderBypass (earlier today,
        // same task) copied this same flawed IS-NULL-OR shape from the 2026-06-29 precedent
        // instead of the actually-correct canonical pattern I was briefed with — six of its
        // eleven tables (customers, schedule_shifts, work_schedules, support_tickets,
        // ticket_comments, chat_sessions) are included in this fix.
        //
        // NOT touched here — 3 tables where the fail-open branch is a deliberate, load-bearing
        // design choice, not a bug: `users` (login must look up a user by email before the
        // caller's tenant is known — see FixUsersRlsNullIfEmptyString's own comment) and
        // `refresh_tokens`/`notification_settings` (same reasoning, added in the same original
        // migration, for the token-refresh flow that likewise runs before tenant_id is known).
        // Blindly stripping the fail-open branch from these three would break login and token
        // refresh outright — a self-inflicted new P0 while fixing this one.
        //
        // Group A: tables with a direct "TenantId" column and no other special clause — safe to
        // fix generically since the replacement text is identical modulo table name.
        private const string GroupATables = @"
        'ai_order_suggestions', 'as_customers', 'as_service_catalog', 'as_vehicles',
        'as_work_order_lines', 'as_work_orders', 'categories', 'chat_sessions', 'customers',
        'daily_sales', 'demand_events', 'discounts', 'integration_configs', 'iot_devices', 'items',
        'locations', 'pos_shifts', 'pos_transactions', 'product_adu', 'product_buffer',
        'product_segments', 'product_stock', 'product_supplier_settings', 'production_orders',
        'promo_cannibalization', 'recipes', 'schedule_shifts', 'stock_events', 'stock_movements',
        'stock_receipts', 'stock_transfers', 'supplier_item_barcodes', 'supplier_item_images',
        'supplier_items', 'supplier_metrics', 'supplier_profiles', 'supplier_reviews',
        'suppliers', 'supply_schedules', 'support_tickets', 'ticket_comments',
        'weather_coefficients', 'work_schedules', 'write_offs'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Group A — generic fail-closed replacement.
            migrationBuilder.Sql($@"
DO $fail_closed_group_a$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[{GroupATables}
    ]
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', t);
        EXECUTE format(
          'CREATE POLICY tenant_isolation ON %I USING (""TenantId"" = (NULLIF(current_setting(''app.tenant_id'', true), ''''))::uuid)',
          t);
    END LOOP;
END $fail_closed_group_a$;
");

            // Group B — direct TenantId column, but NULL TenantId is a legitimate data state
            // (provider/system-wide rows) that must stay visible; only the session-level
            // fail-open branch is removed.
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON activity_logs;
CREATE POLICY tenant_isolation ON activity_logs USING (
  ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
  OR ""TenantId"" IS NULL
);

DROP POLICY IF EXISTS tenant_isolation ON notification_queue;
CREATE POLICY tenant_isolation ON notification_queue USING (
  ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
  OR ""TenantId"" IS NULL
);
");

            // Group C — EXISTS-through-parent tables; same fix, parent lookup unchanged, only
            // the leading session-level fail-open branch removed.
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON ai_order_suggestion_items;
CREATE POLICY tenant_isolation ON ai_order_suggestion_items USING (
  EXISTS (SELECT 1 FROM ai_order_suggestions s WHERE s.""Id"" = ai_order_suggestion_items.""SuggestionId""
    AND s.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);

DROP POLICY IF EXISTS tenant_isolation ON demand_event_coefficients;
CREATE POLICY tenant_isolation ON demand_event_coefficients USING (
  EXISTS (SELECT 1 FROM demand_events ev WHERE ev.""Id"" = demand_event_coefficients.""EventId""
    AND ev.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);

DROP POLICY IF EXISTS tenant_isolation ON location_zones;
CREATE POLICY tenant_isolation ON location_zones USING (
  EXISTS (SELECT 1 FROM locations l WHERE l.""Id"" = location_zones.""LocationId""
    AND l.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);

DROP POLICY IF EXISTS tenant_isolation ON pos_transaction_items;
CREATE POLICY tenant_isolation ON pos_transaction_items USING (
  EXISTS (SELECT 1 FROM pos_transactions t WHERE t.""Id"" = pos_transaction_items.""TransactionId""
    AND t.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);

DROP POLICY IF EXISTS tenant_isolation ON stock_receipt_items;
CREATE POLICY tenant_isolation ON stock_receipt_items USING (
  EXISTS (SELECT 1 FROM stock_receipts r WHERE r.""Id"" = stock_receipt_items.""ReceiptId""
    AND r.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);

DROP POLICY IF EXISTS tenant_isolation ON stock_transfer_items;
CREATE POLICY tenant_isolation ON stock_transfer_items USING (
  EXISTS (SELECT 1 FROM stock_transfers t WHERE t.""Id"" = stock_transfer_items.""TransferId""
    AND t.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);

DROP POLICY IF EXISTS tenant_isolation ON telegram_link_codes;
CREATE POLICY tenant_isolation ON telegram_link_codes USING (
  EXISTS (SELECT 1 FROM users u WHERE u.""Id"" = telegram_link_codes.""UserId""
    AND u.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);

DROP POLICY IF EXISTS tenant_isolation ON temperature_readings;
CREATE POLICY tenant_isolation ON temperature_readings USING (
  EXISTS (SELECT 1 FROM iot_devices d WHERE d.""Id"" = temperature_readings.""DeviceId""
    AND d.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);

DROP POLICY IF EXISTS tenant_isolation ON weather_data;
CREATE POLICY tenant_isolation ON weather_data USING (
  EXISTS (SELECT 1 FROM locations s WHERE s.""Id"" = weather_data.""LocationId""
    AND s.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);

DROP POLICY IF EXISTS tenant_isolation ON weight_readings;
CREATE POLICY tenant_isolation ON weight_readings USING (
  EXISTS (SELECT 1 FROM iot_devices d WHERE d.""Id"" = weight_readings.""DeviceId""
    AND d.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);

DROP POLICY IF EXISTS tenant_isolation ON write_off_items;
CREATE POLICY tenant_isolation ON write_off_items USING (
  EXISTS (SELECT 1 FROM write_offs w WHERE w.""Id"" = write_off_items.""WriteOffId""
    AND w.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverting would reintroduce the fail-open cross-tenant leak on RESET — intentionally
            // left as a no-op, same precedent as 20260629010000_FixAllRlsPoliciesNullIfEmptyString.
        }
    }
}
