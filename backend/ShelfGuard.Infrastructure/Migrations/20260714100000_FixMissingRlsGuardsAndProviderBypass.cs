using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260714100000_FixMissingRlsGuardsAndProviderBypass")]
    public partial class FixMissingRlsGuardsAndProviderBypass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Found in Block 2 pre-launch DB audit (2026-07-14): the two earlier bulk
            // NULLIF-guard fixes (20260629000000_FixUsersRlsNullIfEmptyString,
            // 20260629010000_FixAllRlsPoliciesNullIfEmptyString) only targeted policies
            // literally named 'tenant_isolation'. Six tables were created with a different
            // ad-hoc tenant-policy name (customers_tenant_isolation, schedule_shifts_tenant,
            // work_schedules_tenant, support_tickets_tenant, ticket_comments_tenant,
            // chat_sessions_tenant) and were silently skipped by both bulk fixes — five of
            // them still cast current_setting('app.tenant_id') straight to uuid with no
            // NULLIF guard at all, and chat_sessions used an OR-based guard
            // (`... OR current_setting(...) = ''`) that looks equivalent but does NOT
            // short-circuit the cast in the first branch — reproduced locally: querying
            // any of the six as a non-superuser (NOSUPERUSER/NOBYPASSRLS) app role with
            // app.tenant_id RESET (the state used for every unauthenticated request, see
            // TenantConnectionInterceptor.GetSetSql) raises
            // "invalid input syntax for type uuid: ''" and the query fails — the same class
            // of production incident the 2026-06-29 migrations fixed for `users`, just
            // missed here due to the name mismatch. Confirmed this is not masked by
            // worker_bypass/provider_bypass: PostgreSQL evaluates every permissive policy's
            // qual, so the bad cast throws regardless of which policy would otherwise grant
            // access.
            //
            // Additionally: customers, schedule_shifts, and work_schedules had no
            // provider_bypass policy at all (unlike every other FORCE RLS table) — added
            // for consistency with the canonical pattern in
            // .claude/agents/database-engineer.md. support_tickets/ticket_comments/
            // chat_sessions already have a (deliberately scoped, see
            // 20260623010000_ExpandProviderBypassRlsForTeam) provider_bypass and are left
            // untouched here.
            //
            // Renamed each ad-hoc policy to the canonical 'tenant_isolation' name so a
            // future `WHERE policyname = 'tenant_isolation'` bulk fix can no longer miss
            // these tables.
            //
            // Additive/corrective only — no data migration, no downtime.
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS customers_tenant_isolation ON customers;
CREATE POLICY tenant_isolation ON customers
  USING (
    NULLIF(current_setting('app.tenant_id', true), '') IS NULL
    OR ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
  );
DROP POLICY IF EXISTS provider_bypass ON customers;
CREATE POLICY provider_bypass ON customers
  USING (current_setting('app.role', true) = 'provider');

DROP POLICY IF EXISTS schedule_shifts_tenant ON schedule_shifts;
CREATE POLICY tenant_isolation ON schedule_shifts
  USING (
    NULLIF(current_setting('app.tenant_id', true), '') IS NULL
    OR ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
  );
DROP POLICY IF EXISTS provider_bypass ON schedule_shifts;
CREATE POLICY provider_bypass ON schedule_shifts
  USING (current_setting('app.role', true) = 'provider');

DROP POLICY IF EXISTS work_schedules_tenant ON work_schedules;
CREATE POLICY tenant_isolation ON work_schedules
  USING (
    NULLIF(current_setting('app.tenant_id', true), '') IS NULL
    OR ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
  );
DROP POLICY IF EXISTS provider_bypass ON work_schedules;
CREATE POLICY provider_bypass ON work_schedules
  USING (current_setting('app.role', true) = 'provider');

DROP POLICY IF EXISTS support_tickets_tenant ON support_tickets;
CREATE POLICY tenant_isolation ON support_tickets
  USING (
    NULLIF(current_setting('app.tenant_id', true), '') IS NULL
    OR ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
  );

DROP POLICY IF EXISTS ticket_comments_tenant ON ticket_comments;
CREATE POLICY tenant_isolation ON ticket_comments
  USING (
    NULLIF(current_setting('app.tenant_id', true), '') IS NULL
    OR ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
  );

DROP POLICY IF EXISTS chat_sessions_tenant ON chat_sessions;
CREATE POLICY tenant_isolation ON chat_sessions
  USING (
    NULLIF(current_setting('app.tenant_id', true), '') IS NULL
    OR ""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
  );
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverting would reintroduce the uuid-cast crash and drop the
            // customers/schedule_shifts/work_schedules provider_bypass policies —
            // intentionally left as a no-op, same precedent as
            // 20260629010000_FixAllRlsPoliciesNullIfEmptyString.
        }
    }
}
