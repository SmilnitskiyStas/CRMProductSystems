using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260716120000_FixActivityLogsInsertUnderFailClosedRls")]
    public partial class FixActivityLogsInsertUnderFailClosedRls : Migration
    {
        // Hotfix for a production regression introduced by 20260714180000_FixFailOpenTenantIsolationOnReset.
        //
        // ROOT CAUSE OF A SECOND BUG FOUND WHILE DEPLOYING THIS FIX: this file was originally
        // written without the [DbContext]/[Migration] attributes above. Without [Migration("id")],
        // EF Core's migrations-assembly scanner does not register the class as belonging to
        // AppDbContext's migration set at all, so `MigrateAsync()` silently never saw it as
        // pending — logged "No migrations were applied. The database is already up to date."
        // even though the compiled DLL (verified rebuilt correctly, image/DLL timestamps matched
        // the deploy window) genuinely contained this class. Confirmed via `grep -a` on the
        // running container's ShelfGuard.Infrastructure.dll — the class WAS present, so this was
        // never a Docker build-caching problem, only a missing-attribute bug in this file. Applied
        // manually to production instead (DROP/CREATE POLICY + manual __EFMigrationsHistory
        // insert, user-authorized) while this attribute fix rides along in the next deploy.
        // That migration made activity_logs / notification_queue's tenant_isolation policy fail-closed
        // using a `FOR ALL ... USING (...)` policy with NO `WITH CHECK`. Postgres then applies the USING
        // expression as the INSERT check as well — so a row written with a non-null "TenantId" while the
        // connection has no app.tenant_id set (the anonymous/pre-tenant context: a *successful* login writes
        // an activity_logs audit row stamped with the user's TenantId before TenantConnectionInterceptor has
        // any tenant claim to set) is rejected: 42501 "new row violates row-level security policy". This was
        // live on prod (222 failures in the first 3h post-deploy, activity_logs only).
        //
        // Fix: keep USING fail-closed (SELECT/UPDATE/DELETE stay tenant-scoped — the actual cross-tenant
        // READ leak that 20260714180000 fixed is fully preserved) and add a permissive WITH CHECK so INSERTs
        // succeed when app.tenant_id is unset. A permissive INSERT is not a data-disclosure risk — SELECT was
        // the leak vector, and the application stamps the row's TenantId itself; the worst case is audit-row
        // attribution, not cross-tenant data exposure. worker_bypass / provider_bypass policies are untouched.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON activity_logs;
CREATE POLICY tenant_isolation ON activity_logs
  FOR ALL
  USING (
    ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
    OR ""TenantId"" IS NULL
  )
  WITH CHECK (
    NULLIF(current_setting('app.tenant_id', true), '') IS NULL
    OR ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
    OR ""TenantId"" IS NULL
  );

DROP POLICY IF EXISTS tenant_isolation ON notification_queue;
CREATE POLICY tenant_isolation ON notification_queue
  FOR ALL
  USING (
    ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
    OR ""TenantId"" IS NULL
  )
  WITH CHECK (
    NULLIF(current_setting('app.tenant_id', true), '') IS NULL
    OR ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
    OR ""TenantId"" IS NULL
  );
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: reverting would reintroduce the production-breaking INSERT rejection.
        }
    }
}
