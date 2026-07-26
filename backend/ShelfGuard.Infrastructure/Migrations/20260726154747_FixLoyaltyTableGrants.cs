using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// TASK-411 hotfix. TASK-404's <c>AddLoyaltyProgram</c> migration (20260726132332) was
    /// applied to dev by connecting directly as the <c>crm</c> bootstrap superuser (that
    /// migration's own task log: "Migration applied to dev DB via `crm` superuser
    /// (FK-validation-under-RLS gotcha)"), instead of through the app's own restricted
    /// connection the way every migration since the KI-027 fix (TASK-372, 20260716) normally
    /// runs. This codebase has no bootstrap script and no <c>ALTER DEFAULT PRIVILEGES</c>
    /// anywhere — every table's access to the real app role comes purely from table
    /// OWNERSHIP: TASK-372 bulk-transferred ownership of the 84 tables that existed at the
    /// time via a one-time <c>ALTER TABLE ... OWNER TO</c> loop, and every table created since
    /// inherited correct ownership automatically because the migration that created it ran
    /// through the app's own (already-owning) connection. Verified live: <c>tenants</c>,
    /// <c>customers</c>, <c>pos_transactions</c>, <c>users</c>, and every other table created
    /// after 20260716 (<c>chat_sessions</c>, <c>supply_schedules</c>, <c>tenant_roles</c>,
    /// <c>user_locations</c>, <c>user_permission_grants</c>) are owned by the dev app role;
    /// the 4 new loyalty tables are the only ones owned by <c>crm</c> instead, with ZERO
    /// grants to the app role — confirmed live via <c>information_schema.table_privileges</c>.
    ///
    /// Symptom (TASK-410, reproduced directly here too): connecting as the real app role and
    /// querying any of the 4 tables fails with Postgres 42501
    /// "permission denied for table loyalty_ledger_entries" — which is exactly what
    /// <c>GET /api/pos/sales</c> hits in production code, since TASK-410 wired
    /// <c>GetSalesForShiftAsync</c> to read <c>LoyaltyLedgerEntry</c>.
    ///
    /// Fix: transfer ownership of exactly these 4 tables to whichever role currently owns
    /// <c>tenants</c> — i.e. this environment's already-established app role
    /// (<c>shelfguard_app_dev</c> in dev, <c>shelfguard_staging_app</c> in staging, whatever
    /// production's is) — rather than hardcoding one environment's role name into a migration
    /// that must also run correctly in the other two. Touches only these 4 tables; no other
    /// table's ownership, grants, RLS policy, or schema changes.
    ///
    /// Operational note (see task log 411 for full detail): like <c>AddLoyaltyProgram</c>
    /// itself, <c>ALTER TABLE ... OWNER TO</c> requires the executing role to be either a
    /// superuser or the table's CURRENT owner — the restricted app role is neither for these
    /// 4 tables (it isn't <c>crm</c> and doesn't already own them), so it cannot even grant
    /// itself ownership. This migration MUST be applied via a superuser connection (the same
    /// way <c>AddLoyaltyProgram</c> was), NOT left to the app's normal boot-time
    /// <c>Database.MigrateAsync()</c> — that call runs as the restricted app role in every
    /// environment (see <c>docs/staging.md</c> "Migrations and seed data") and this statement
    /// would fail with "must be owner of relation" before the app finished starting.
    /// </summary>
    public partial class FixLoyaltyTableGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                  app_owner text;
                BEGIN
                  SELECT tableowner INTO app_owner
                  FROM pg_tables
                  WHERE schemaname = 'public' AND tablename = 'tenants';

                  IF app_owner IS NULL THEN
                    RAISE EXCEPTION
                      'FixLoyaltyTableGrants: could not resolve owner of table ""tenants"" — aborting rather than guessing a role name';
                  END IF;

                  EXECUTE format('ALTER TABLE consumer_accounts OWNER TO %I', app_owner);
                  EXECUTE format('ALTER TABLE loyalty_program_settings OWNER TO %I', app_owner);
                  EXECUTE format('ALTER TABLE loyalty_memberships OWNER TO %I', app_owner);
                  EXECUTE format('ALTER TABLE loyalty_ledger_entries OWNER TO %I', app_owner);
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op: reverting would hand these 4 tables' ownership back to
            // the migration superuser, silently reintroducing the exact 42501
            // permission-denied bug this migration fixes. Same precedent as
            // FixAllRlsPoliciesNullIfEmptyString (20260629010000), which leaves its own
            // Down() empty for the same class of reason (documented there in comments).
        }
    }
}
