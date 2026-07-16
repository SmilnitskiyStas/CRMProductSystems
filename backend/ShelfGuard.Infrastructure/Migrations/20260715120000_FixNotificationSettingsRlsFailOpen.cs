using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260715120000_FixNotificationSettingsRlsFailOpen")]
    public partial class FixNotificationSettingsRlsFailOpen : Migration
    {
        // P0 found and live-reproduced 2026-07-15 (TASK-360, Block 9 pre-launch audit —
        // Customers/Notifications/Schedules).
        //
        // 20260629010000_FixAllRlsPoliciesNullIfEmptyString added a session-level fail-open
        // branch (`NULLIF(current_setting('app.tenant_id', true), '') IS NULL OR ...`) to
        // notification_settings' tenant_isolation policy. 20260714180000_
        // FixFailOpenTenantIsolationOnReset (Block 2, TASK-352) removed that exact branch from
        // 57 other tables as a P0 cross-tenant leak, but deliberately left notification_settings
        // untouched — its own comment groups it with `users`/`refresh_tokens` as having a
        // "legitimate pre-auth lookup need", same reasoning as the token-refresh flow.
        //
        // That reasoning does not actually hold for notification_settings: unlike
        // refresh_tokens (looked up by opaque token value at POST /api/auth/refresh, an
        // [AllowAnonymous] endpoint, before the caller's tenant is known) and users (looked up
        // by email at POST /api/auth/login, also anonymous), every notification_settings access
        // is NotificationsController.GetSettings/UpsertSetting — both `[Authorize]`, both
        // resolve UserId from an already-validated JWT. By the time either handler runs,
        // ASP.NET Core's auth middleware has already authenticated the request and
        // TenantConnectionInterceptor has already SET app.tenant_id from the JWT's tenant_id
        // claim (see TenantConnectionInterceptor.GetSetSql — only unauthenticated requests get
        // RESET). There is no anonymous code path that touches this table.
        //
        // Live-reproduced with the real non-superuser app role (shelfguard_app) and
        // SESSION AUTHORIZATION + RESET app.tenant_id/app.role (the exact state Block 2's own
        // audit query used): two rows seeded for users in two different tenants were BOTH
        // visible in a single SELECT — the fail-open branch really does leak cross-tenant here,
        // it just went unnoticed because the table happened to be empty at audit time.
        //
        // Fix: remove only the outer session-level fail-open branch. Keep the inner
        // `OR u."TenantId" IS NULL` — provider accounts (users.TenantId IS NULL by design) get
        // app.tenant_id set to the null-uuid sentinel (TenantConnectionInterceptor.BuildSetSql),
        // not RESET, so they still need this branch to manage their own notification settings.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON notification_settings;
CREATE POLICY tenant_isolation ON notification_settings USING (
  EXISTS (SELECT 1 FROM users u WHERE u.""Id"" = notification_settings.""UserId""
    AND (u.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
         OR u.""TenantId"" IS NULL))
);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverting would reintroduce the fail-open cross-tenant leak on RESET —
            // intentionally left as a no-op, same precedent as the two migrations referenced
            // above.
        }
    }
}
