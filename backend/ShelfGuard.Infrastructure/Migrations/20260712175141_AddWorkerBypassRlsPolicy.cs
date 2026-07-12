using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerBypassRlsPolicy : Migration
    {
        // Audited from every migration under backend/ShelfGuard.Infrastructure/Migrations
        // as of 2026-07-12: the full list of tables that currently have FORCE ROW LEVEL
        // SECURITY (73 of the 82 tables tracked by AppDbContext — the other 9 have no RLS
        // at all: Products (legacy/unused), chat_messages, support_messages,
        // production_order_consumptions, provider_roles, provider_schedule_slots,
        // recipe_ingredients, landing_leads, tenants).
        private const string TableArray = @"
        'users', 'refresh_tokens', 'notification_settings', 'integration_configs',
        'locations', 'location_zones', 'categories', 'product_segments', 'suppliers',
        'items', 'product_supplier_settings', 'product_stock', 'stock_movements',
        'stock_events', 'stock_receipts', 'stock_receipt_items', 'stock_transfers',
        'stock_transfer_items', 'write_offs', 'write_off_items', 'discounts',
        'notification_queue', 'activity_logs', 'daily_sales', 'product_adu',
        'supply_schedules', 'product_buffer', 'demand_events', 'weather_coefficients',
        'demand_event_coefficients', 'weather_data', 'ai_order_suggestions',
        'ai_order_suggestion_items', 'promo_cannibalization', 'pos_shifts',
        'pos_transactions', 'pos_transaction_items', 'iot_devices',
        'temperature_readings', 'weight_readings', 'telegram_link_codes',
        'supplier_profiles', 'supplier_items', 'supplier_metrics', 'supplier_reviews',
        'recipes', 'production_orders', 'as_customers', 'as_vehicles',
        'as_service_catalog', 'as_work_orders', 'as_work_order_lines', 'customers',
        'work_schedules', 'schedule_shifts', 'support_tickets', 'ticket_comments',
        'chat_sessions', 'supplier_item_barcodes', 'supplier_item_images',
        'supplier_chat_sessions', 'supplier_chat_messages', 'supplier_roles',
        'supplier_tasks', 'legal_entities', 'supplier_contract_settings',
        'supplier_agreements', 'marketplace_orders', 'marketplace_order_items',
        'supplier_support_tickets', 'supplier_support_ticket_messages',
        'stock_status_snapshots', 'user_permission_grants'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HOTFIX (TASK-343 / follow-up to TASK-342 security review):
            // Worker cron jobs (worker/src/jobs/*.job.ts) run raw SQL against
            // RLS-protected tenant tables after `SET app.role = 'worker'`, but no
            // RLS policy recognizes that role — only `provider_bypass` exists.
            // Combined with FORCE ROW LEVEL SECURITY (see
            // 20260628000000_ForceRlsOnAllTenantTables), every worker write against
            // a table whose tenant_isolation policy has no permissive fallback is
            // silently blocked by Postgres. Confirmed in prod via direct SQL query:
            // stock_status_snapshots had 0 rows despite the nightly worker job
            // running successfully (no error — RLS just filters/rejects the rows).
            //
            // This adds a `worker_bypass` policy (same shape as the existing
            // `provider_bypass`) to every table that currently has FORCE ROW LEVEL
            // SECURITY — not just the tables the worker touches today, so newly
            // added FORCE RLS tables aren't silently missed later.
            //
            // Additive only: does not touch existing tenant_isolation / provider_bypass
            // policies, no data migration, no downtime.
            migrationBuilder.Sql($@"
DO $worker_bypass$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[{TableArray}
    ]
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS worker_bypass ON %I', t);
        EXECUTE format('CREATE POLICY worker_bypass ON %I USING (current_setting(''app.role'', true) = ''worker'')', t);
    END LOOP;
END $worker_bypass$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
DO $worker_bypass_down$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[{TableArray}
    ]
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS worker_bypass ON %I', t);
    END LOOP;
END $worker_bypass_down$;
");
        }
    }
}
