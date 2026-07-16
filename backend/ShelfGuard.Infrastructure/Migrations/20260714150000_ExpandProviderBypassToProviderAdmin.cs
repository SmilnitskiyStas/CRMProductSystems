using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260714150000_ExpandProviderBypassToProviderAdmin")]
    public partial class ExpandProviderBypassToProviderAdmin : Migration
    {
        // Audited from pg_policies as of 2026-07-14 (Block 2 pre-launch audit follow-up, user
        // confirmed in chat): every FORCE RLS table whose provider_bypass policy currently
        // matches ONLY `app.role = 'provider'`. ProviderPermissions.SystemRoleDefaults grants
        // AppRoles.ProviderAdmin the exact same `All` permission set as AppRoles.Provider, and
        // TenantConnectionInterceptor.ValidRoles already whitelists "provider_admin" and sets
        // `app.role = 'provider_admin'` on the connection for such users (verified — no
        // interceptor change needed) — so a provider_admin team member currently gets silently
        // empty results (RLS fails closed, not a leak) on every one of these 71 tables despite
        // having the equivalent app-level permission. Three tables (support_tickets,
        // ticket_comments, chat_sessions) were already expanded earlier
        // (20260623010000_ExpandProviderBypassRlsForTeam) to also include provider_agent for
        // their narrower ServiceDesk/LiveChat scope — deliberately NOT touched here, since
        // provider_agent's permission set (ProviderPermissions: ViewClients, ServiceDesk,
        // LiveChat only) does not extend to these 71 tables.
        private const string TableArray = @"
        'activity_logs', 'ai_order_suggestion_items', 'ai_order_suggestions', 'as_customers',
        'as_service_catalog', 'as_vehicles', 'as_work_order_lines', 'as_work_orders', 'categories',
        'customers', 'daily_sales', 'demand_event_coefficients', 'demand_events', 'discounts',
        'integration_configs', 'iot_devices', 'items', 'legal_entities', 'location_zones',
        'locations', 'marketplace_order_items', 'marketplace_orders', 'notification_queue',
        'notification_settings', 'pos_shifts', 'pos_transaction_items', 'pos_transactions',
        'product_adu', 'product_buffer', 'product_segments', 'product_stock',
        'product_supplier_settings', 'production_orders', 'promo_cannibalization', 'recipes',
        'refresh_tokens', 'schedule_shifts', 'stock_events', 'stock_movements',
        'stock_receipt_items', 'stock_receipts', 'stock_status_snapshots', 'stock_transfer_items',
        'stock_transfers', 'supplier_agreements', 'supplier_chat_messages',
        'supplier_chat_sessions', 'supplier_contract_settings', 'supplier_item_barcodes',
        'supplier_item_images', 'supplier_items', 'supplier_metrics', 'supplier_profiles',
        'supplier_reviews', 'supplier_roles', 'supplier_support_ticket_messages',
        'supplier_support_tickets', 'supplier_tasks', 'suppliers', 'supply_schedules',
        'telegram_link_codes', 'temperature_readings', 'tenant_roles', 'user_permission_grants',
        'users', 'weather_coefficients', 'weather_data', 'weight_readings', 'work_schedules',
        'write_off_items', 'write_offs'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // User-confirmed (chat, 2026-07-14): expand provider_bypass on all 71 tables to
            // also accept 'provider_admin', matching ProviderPermissions parity with 'provider'.
            // Additive/corrective only — no data migration, no downtime.
            migrationBuilder.Sql($@"
DO $provider_admin_bypass$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[{TableArray}
    ]
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS provider_bypass ON %I', t);
        EXECUTE format(
          'CREATE POLICY provider_bypass ON %I USING (current_setting(''app.role'', true) IN (''provider'', ''provider_admin''))',
          t);
    END LOOP;
END $provider_admin_bypass$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
DO $provider_admin_bypass_down$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[{TableArray}
    ]
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS provider_bypass ON %I', t);
        EXECUTE format(
          'CREATE POLICY provider_bypass ON %I USING (current_setting(''app.role'', true) = ''provider'')',
          t);
    END LOOP;
END $provider_admin_bypass_down$;
");
        }
    }
}
