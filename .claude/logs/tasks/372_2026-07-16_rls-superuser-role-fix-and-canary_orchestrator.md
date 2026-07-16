# TASK-372 — KI-027 (RLS superuser-role bypass) fix on staging+dev + KI-028 startup canary

**Status:** done (2026-07-16) · **By:** main session (orchestrator, direct) · **Block:** 18 follow-up
**Depends:** TASK-371 (Block 18 security audit, which found KI-027/KI-028)

User authorized both fixes directly in chat (AskUserQuestion: "Так, на staging і dev" for the role
fix; "Додати canary-перевірку при старті" for KI-028). The spawned security agent correctly refused
to act on a relayed approval (per its operating rules — no agent message equals user consent), so the
orchestrator executed this directly, holding genuine in-conversation user authorization.

## KI-027 — non-superuser app role (staging + dev)
Root cause: the app/worker connected to Postgres as the `POSTGRES_USER` bootstrap role, which the
official postgres image makes a cluster superuser — superusers bypass RLS unconditionally, so all
`tenant_isolation`/FORCE-RLS policies were silently inert. The proven production fix (dedicated
non-superuser owner role) was never repeated when staging was stood up in Block 0, and dev's `crm`
had the same property.

Done on **both** stacks:
- Created `shelfguard_staging_app` (staging) / `shelfguard_app_dev` (dev): `NOSUPERUSER NOCREATEDB
  NOCREATEROLE NOBYPASSRLS`, transferred ownership of all 84 public tables + sequences to it, granted
  `CONNECT` + `USAGE, CREATE ON SCHEMA public`.
- Repointed app connections: `.env.staging` `DATABASE_URL`/`WORKER_DATABASE_URL`; dev
  `appsettings.Development.json` `DefaultConnection` + `docker-compose.yml` worker `DATABASE_URL`.
  Bootstrap superusers (`shelfguard_staging`/`crm`) kept for initdb/admin only, now own nothing.
- Restarted api+worker on both stacks.

Verified live (as the app role, not an ad-hoc test role):
- `rolsuper=f, rolbypassrls=f`.
- Scoped to one tenant → that tenant's `items`/`product_stock` rows, **0 cross-tenant leak** (was:
  full cross-tenant read as superuser — the exact IDOR the Block 18 agent reproduced).
- `app.tenant_id` unset (RESET) → **0 rows** (fail-closed).
- `app.role='worker'` → sees all rows (worker_bypass intact — cron jobs still work).
- Dev API boots clean, dev worker connects clean (`[worker] All workers started`, `[mqtt] connected`).

Known follow-up (documented in KI-027, not blocking): `DbSeeder` has only ever run under a superuser;
seeding a *fresh empty* DB as the non-superuser role would hit fail-closed RLS on tenant tables.
Current dev/staging DBs are already seeded so the `Tenants.AnyAsync()` short-circuit means this path
is never taken; prod never seeds (KI-006). Fix when needed: `SET app.role='provider'` around the
seeder's inserts.

## KI-028 — startup RLS-bypass canary
`Program.cs`, right after `MigrateAsync`: queries `rolsuper OR rolbypassrls` for `current_user`.
Decision policy factored into pure `RlsRoleGuard.Evaluate(roleBypassesRls, isDevelopment)`
(`ShelfGuard.Infrastructure/Data/RlsRoleGuard.cs`): bypassing role → **fail-fast (throw) outside
Development**, **log CRITICAL but boot in Development**. 4 unit tests in
`ShelfGuard.Tests/Infrastructure/RlsRoleGuardTests.cs`. Catches this whole class of
misconfiguration automatically in any future environment on boot. Confirmed the dev API now boots
with no canary warning (role is non-superuser → `Decision.Ok`).

## Build/test
`dotnet build` 0 errors. `dotnet test` **854/854** green (was 850 + 4 new canary tests).

## Files
`backend/ShelfGuard.Api/Program.cs`, `backend/ShelfGuard.Infrastructure/Data/RlsRoleGuard.cs` (new),
`backend/ShelfGuard.Tests/Infrastructure/RlsRoleGuardTests.cs` (new),
`backend/ShelfGuard.Api/appsettings.Development.json`, `docker-compose.yml`, `.env.staging`
(gitignored), `.claude/docs/known-issues.md` (KI-027/KI-028 resolved). Production untouched.
