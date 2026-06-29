using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixAllRlsPoliciesNullIfEmptyString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // After switching the app DB user to shelfguard_app (non-superuser), RLS policies
            // are now evaluated for ALL requests including unauthenticated ones (login).
            // TenantConnectionInterceptor runs RESET app.tenant_id for unauthenticated requests.
            // PostgreSQL returns '' (empty string) after RESET for custom GUC variables — NOT NULL.
            // The previous policies cast '' directly to uuid → "invalid input syntax for type uuid".
            // Fix: NULLIF converts '' to NULL before the cast, which short-circuits the condition.
            migrationBuilder.Sql(@"
-- Group 1: simple TenantId equality
DO $g1$
DECLARE t text;
BEGIN
  FOR t IN
    SELECT DISTINCT tablename FROM pg_policies
    WHERE policyname = 'tenant_isolation'
      AND tablename NOT IN (
        'users','activity_logs','notification_queue',
        'ai_order_suggestion_items','demand_event_coefficients',
        'temperature_readings','weight_readings','location_zones',
        'weather_data','pos_transaction_items','stock_receipt_items',
        'stock_transfer_items','notification_settings','refresh_tokens',
        'telegram_link_codes','write_off_items'
      )
  LOOP
    EXECUTE format(
      'ALTER POLICY tenant_isolation ON %I USING (
         NULLIF(current_setting(''app.tenant_id'', true), '''') IS NULL
         OR ""TenantId"" = (NULLIF(current_setting(''app.tenant_id'', true), ''''))::uuid
       )', t);
  END LOOP;
END $g1$;

-- Group 2: TenantId IS NULL variants
ALTER POLICY tenant_isolation ON activity_logs USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
  OR ""TenantId"" IS NULL
);
ALTER POLICY tenant_isolation ON notification_queue USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
  OR ""TenantId"" IS NULL
);

-- Group 3: EXISTS via parent table
ALTER POLICY tenant_isolation ON refresh_tokens USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR EXISTS (SELECT 1 FROM users u WHERE u.""Id"" = refresh_tokens.""UserId""
    AND ((u.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid) OR u.""TenantId"" IS NULL))
);
ALTER POLICY tenant_isolation ON notification_settings USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR EXISTS (SELECT 1 FROM users u WHERE u.""Id"" = notification_settings.""UserId""
    AND ((u.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid) OR u.""TenantId"" IS NULL))
);
ALTER POLICY tenant_isolation ON telegram_link_codes USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR EXISTS (SELECT 1 FROM users u WHERE u.""Id"" = telegram_link_codes.""UserId""
    AND u.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);
ALTER POLICY tenant_isolation ON location_zones USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR EXISTS (SELECT 1 FROM locations l WHERE l.""Id"" = location_zones.""LocationId""
    AND l.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);
ALTER POLICY tenant_isolation ON ai_order_suggestion_items USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR EXISTS (SELECT 1 FROM ai_order_suggestions s WHERE s.""Id"" = ai_order_suggestion_items.""SuggestionId""
    AND s.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);
ALTER POLICY tenant_isolation ON demand_event_coefficients USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR EXISTS (SELECT 1 FROM demand_events ev WHERE ev.""Id"" = demand_event_coefficients.""EventId""
    AND ev.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);
ALTER POLICY tenant_isolation ON temperature_readings USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR EXISTS (SELECT 1 FROM iot_devices d WHERE d.""Id"" = temperature_readings.""DeviceId""
    AND d.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);
ALTER POLICY tenant_isolation ON weight_readings USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR EXISTS (SELECT 1 FROM iot_devices d WHERE d.""Id"" = weight_readings.""DeviceId""
    AND d.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);
ALTER POLICY tenant_isolation ON weather_data USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR EXISTS (SELECT 1 FROM locations s WHERE s.""Id"" = weather_data.""LocationId""
    AND s.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);
ALTER POLICY tenant_isolation ON pos_transaction_items USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR EXISTS (SELECT 1 FROM pos_transactions t WHERE t.""Id"" = pos_transaction_items.""TransactionId""
    AND t.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);
ALTER POLICY tenant_isolation ON stock_receipt_items USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR EXISTS (SELECT 1 FROM stock_receipts r WHERE r.""Id"" = stock_receipt_items.""ReceiptId""
    AND r.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);
ALTER POLICY tenant_isolation ON stock_transfer_items USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR EXISTS (SELECT 1 FROM stock_transfers t WHERE t.""Id"" = stock_transfer_items.""TransferId""
    AND t.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);
ALTER POLICY tenant_isolation ON write_off_items USING (
  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
  OR EXISTS (SELECT 1 FROM write_offs w WHERE w.""Id"" = write_off_items.""WriteOffId""
    AND w.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverting this migration would re-break login — intentionally left as no-op.
            // The NULLIF change is purely additive and safe to keep permanently.
        }
    }
}
