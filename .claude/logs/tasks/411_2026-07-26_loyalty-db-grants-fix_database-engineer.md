# TASK-411: Fix — 4 loyalty tables owned by migration superuser, zero grants to app role

**Agent:** database-engineer
**Date:** 2026-07-26
**Status:** done — fixed and verified live in dev; staging confirmed unaffected (not yet
deployed there); production not independently re-verified (see below), but cannot currently
have this bug either (see "Production" section).

## Root cause (confirmed, not assumed)

Every table in this schema gets its app-role access purely from **table ownership** — this
codebase has no bootstrap script and no `ALTER DEFAULT PRIVILEGES` anywhere. TASK-372
(2026-07-16, KI-027) bulk-transferred ownership of the 84 tables that existed at the time from
the bootstrap superuser (`crm` in dev) to a dedicated non-superuser app role
(`shelfguard_app_dev`) via a one-time `ALTER TABLE ... OWNER TO` loop. Every table created by a
migration *since* then has inherited correct ownership automatically, because the migration ran
through the app's own (already-owning) connection — verified live: `chat_sessions`,
`supply_schedules`, `tenant_roles`, `user_locations`, `user_permission_grants` (all created
2026-07-19/20, after the KI-027 fix) are owned by `shelfguard_app_dev`, same as `tenants`,
`customers`, `pos_transactions`, `users`.

TASK-404's `AddLoyaltyProgram` migration (20260726132332) broke this pattern: its own task log
says it was "applied to dev DB via `crm` superuser" to route around the documented
FK-validation-under-RLS gotcha (`.claude/docs/database-schema.md` line ~365 — adding a FK
against an RLS-protected parent through the restricted role can false-positive `23503` because
`FORCE ROW LEVEL SECURITY` hides every row from a role with no `app.tenant_id` session context).
Net effect: `consumer_accounts`, `loyalty_memberships`, `loyalty_ledger_entries`,
`loyalty_program_settings` ended up owned by `crm`, with **zero grants** to
`shelfguard_app_dev` — confirmed via `information_schema.table_privileges` (only `crm` and a
test-only `rls_audit_test_role` had any rows; `shelfguard_app_dev` had none at all).

Reproduced live and independently, before touching anything:
```
$ psql -U shelfguard_app_dev -d crm -c "SELECT count(*) FROM loyalty_ledger_entries;"
ERROR:  permission denied for table loyalty_ledger_entries
$ psql -U shelfguard_app_dev -d crm -c "SELECT count(*) FROM customers;"
 count
-------
     0
```
Same 42501 TASK-410 hit through the real app on `GET /api/pos/sales`.

Note on the "gotcha" itself: it specifically applies to adding a FK against an
**already-populated** column referencing an RLS table. All 4 loyalty tables were brand new and
empty in that same migration, so the gotcha likely didn't strictly require `crm` here — probably
applied out of defensive precedent rather than necessity. Not going back to relitigate that
(out of scope, and harmless either way); flagged only because it explains why this is very
plausibly a dev-only accident rather than something that will necessarily repeat on
staging/production (see "Deploy risk" below).

## Fix

New migration `FixLoyaltyTableGrants` (`20260726154747_FixLoyaltyTableGrants.cs`), scoped to
exactly these 4 tables:
```sql
DO $$
DECLARE
  app_owner text;
BEGIN
  SELECT tableowner INTO app_owner FROM pg_tables
  WHERE schemaname = 'public' AND tablename = 'tenants';

  IF app_owner IS NULL THEN
    RAISE EXCEPTION 'FixLoyaltyTableGrants: could not resolve owner of table "tenants" — aborting rather than guessing a role name';
  END IF;

  EXECUTE format('ALTER TABLE consumer_accounts OWNER TO %I', app_owner);
  EXECUTE format('ALTER TABLE loyalty_program_settings OWNER TO %I', app_owner);
  EXECUTE format('ALTER TABLE loyalty_memberships OWNER TO %I', app_owner);
  EXECUTE format('ALTER TABLE loyalty_ledger_entries OWNER TO %I', app_owner);
END $$;
```
Deliberately **dynamic**, not hardcoded to `shelfguard_app_dev`: it copies whichever role
currently owns `tenants` (the established, already-correct app role in *any* environment —
`shelfguard_app_dev` in dev, `shelfguard_staging_app` in staging, whatever production's is).
Hardcoding the dev role name would have broken the migration the moment it reached staging or
production. This also makes the migration self-limiting: if a table it targets is somehow
already correctly owned, the statement is a harmless no-op (owner unchanged).

`Down()` is an intentional no-op (same precedent as `FixAllRlsPoliciesNullIfEmptyString`,
20260629010000) — reverting would hand ownership back to the superuser and silently
reintroduce the exact bug this fixes.

Touches **only** these 4 tables. Verified before/after: `SELECT tablename FROM pg_tables WHERE
schemaname='public' AND tableowner <> 'shelfguard_app_dev'` → 0 rows after the fix (was 4 before:
exactly the loyalty tables, nothing else). `rls_audit_test_role`'s pre-existing test-fixture
grants on the 4 tables are untouched (`ALTER ... OWNER TO` doesn't revoke other roles' grants).
No sequences involved (all 4 tables use `gen_random_uuid()`, not `SERIAL`).

**Side effect, expected and correct, not a scope creep:** scaffolding via `dotnet ef migrations
add` also regenerated `AppDbContextModelSnapshot.cs` to include the 4 loyalty entities — that
aggregate snapshot file had never been updated when `AddLoyaltyProgram` was hand-authored
(its own per-migration `.Designer.cs` already had the correct model; only the separate
"current state" snapshot file was stale). Pure EF metadata bookkeeping fix, zero DB/schema
impact, but necessary for the next `migrations add` to diff correctly.

## Deploy risk — must apply as superuser, not via automatic boot-time MigrateAsync()

`ALTER TABLE ... OWNER TO` requires the executing role to be either a superuser or the table's
**current** owner. The restricted app role is neither for these 4 tables right now (it isn't
`crm` and doesn't already own them) — so it cannot even grant itself ownership. This migration
**must** be applied via a superuser connection, exactly like `AddLoyaltyProgram` itself was.

This matters for staging/production specifically because `docs/staging.md` documents that the
API container runs `Database.MigrateAsync()` unconditionally on every boot, using the app's own
restricted connection. If `AddLoyaltyProgram` + `FixLoyaltyTableGrants` are just deployed as
code and left to that automatic path:
- If `AddLoyaltyProgram` happens to apply cleanly via the restricted role there (plausible per
  the note above — the gotcha needs a populated parent column, not an empty new table), the 4
  tables would already be correctly owned from creation, and `FixLoyaltyTableGrants` would be a
  harmless no-op. Fine.
- If it does **not** (gotcha bites, or whoever runs it manually reaches for a superuser
  connection the same way dev did), the tables end up superuser-owned again, and
  `FixLoyaltyTableGrants`'s `ALTER TABLE ... OWNER TO` statement will fail under the restricted
  role with "must be owner of relation" — at boot, before the app finishes starting.

Applied to dev via the documented superuser escape hatch (`dotnet ef database update
--connection "Host=localhost;Port=5435;Database=crm;Username=crm;Password=***"`), same pattern
`docs/staging.md` already documents for staging (`Username=shelfguard_staging`, the bootstrap
superuser). **Recommend the same manual superuser `dotnet ef database update` step for both
`AddLoyaltyProgram` and `FixLoyaltyTableGrants` when this reaches staging and production**,
rather than trusting the automatic boot path for this specific pair of migrations — removes the
ambiguity above entirely and avoids a boot crash-loop risk either way.

## Verification performed

1. **Live psql as the real app role** (not superuser, not a test role) — `SELECT`/`INSERT` on
   all 4 tables inside a rolled-back transaction, with `app.tenant_id`/`app.role` session vars
   set the way `TenantConnectionInterceptor` sets them for a real request: all 4 inserts
   succeeded, RLS still correctly enforced (confirmed separately that inserting into
   `loyalty_program_settings` *without* `app.tenant_id` set is correctly rejected by
   `tenant_isolation`, not silently allowed — ownership fix didn't weaken RLS). Rolled back, no
   test data left in dev.
2. **Live end-to-end through the actual running API** (not a test harness, not a superuser
   connection) — started `ShelfGuard.Api` locally against the real dev stack, logged in as the
   seeded `manager@demo.local` (store_manager), opened a real shift, created a real sale
   (Рис круглозернистий Чумак 1кг, non-expired batch), then called
   `GET /api/pos/sales?shiftId=...` — **the exact call TASK-410 found 500ing with 42501**.
   Result: `200 OK` with the real transaction back, no 42501, no server-side exception in the
   API log. Shift closed afterward, API process stopped — dev left clean.
3. **`dotnet build`** — 0 errors (1 pre-existing unrelated warning in
   `MarketplaceServiceTests.cs`, predates this task).
4. **`dotnet test`** — **1086/1086 green**, unchanged from TASK-410's baseline (no new tests
   added — this is a permissions-only fix, no new behavior to cover; see the testing-gap note
   below for why the existing suite didn't catch the original bug).

## Staging / production

**Staging** (checked read-only via the already-running local `shelfguard_staging_postgres`
container, no changes made): last applied migration is `20260719193545` — `AddLoyaltyProgram`
has not reached staging yet, so **staging does not have this bug today** (the tables don't exist
there). All 85 existing staging tables are uniformly owned by `shelfguard_staging_app` — no
ownership drift to worry about there yet.

**Production:** confirmed via `git log --all --oneline | grep -i loyalty` — empty. Nothing
loyalty-related (TASK-404 through this task) has ever been committed, and `main`'s current HEAD
(`3c360b26`) predates TASK-404 entirely. Since production only ever receives code through the
normal commit → push → deploy pipeline, **production cannot have these 4 tables or this bug
today, independent of any environment-specific check** — there is nothing there to be broken
yet. Attempted an additional direct SSH read-only confirmation of production's Postgres role
setup anyway (same server as ShelfGuard prod per `.claude/private/access.md`); blocked by the
harness's own permission classifier before it ran anything (same restriction TASK-371/372 hit
attempting a live fix there) — did not attempt to work around it. Not independently
re-verified beyond the git-history argument above; flagging per the task brief's own
instruction for this exact case. **Action item for whoever deploys this:** apply both
`AddLoyaltyProgram` and `FixLoyaltyTableGrants` via the superuser `dotnet ef database update
--connection` path on staging and production, per "Deploy risk" above, rather than relying on
the automatic boot-time migration alone.

## Flagged, not fixed (out of this task's narrow scope)

- **Testing gap:** `dotnet test` stayed green (1086/1086) through this entire incident — TASK-404
  through TASK-410 all reported clean test runs despite the app being non-functional through its
  real connection. Reason: the live-Postgres integration tests
  (`LoyaltyRlsIntegrationTests`/`RlsCrossTenantIntegrationTests`) connect using their own test
  roles (e.g. `rls_audit_test_role`, which — confirmed live — already had explicit grants on the
  4 loyalty tables, unlike `shelfguard_app_dev`), not the actual deployed app connection string.
  A regression like this (new RLS table, correct policies, wrong owner) is invisible to the
  current test suite by construction. Worth a follow-up: one live-Postgres test that connects
  using the *same* connection string `appsettings.Development.json` configures for the app
  (`shelfguard_app_dev`) and asserts basic `SELECT` access on every FORCE RLS table, so this
  exact class of bug fails `dotnet test` next time instead of only surfacing in a live
  `GET` call. Not implemented here — deliberately avoided expanding an urgent, narrow fix into
  new test infrastructure.
- **Docs cross-reference:** `.claude/docs/known-issues.md` already has detailed KI-027/KI-028
  entries for the general "restricted role vs. superuser ownership" bug class; a short addendum
  there linking to this incident would help future agents recognize the pattern faster.
  Not done here (docs update wasn't in this task's scope) — candidate for documentation-writer.

## Files

- `backend/ShelfGuard.Infrastructure/Migrations/20260726154747_FixLoyaltyTableGrants.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260726154747_FixLoyaltyTableGrants.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (regenerated,
  loyalty-entity metadata catch-up only, see above)

Not committed (repo convention — main session/user commits). Migration applied live to the dev
DB only, via the `crm` superuser connection.
