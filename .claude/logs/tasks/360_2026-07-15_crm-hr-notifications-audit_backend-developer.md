---
task_id: TASK-360
date: 2026-07-15
agent: backend-developer (main session)
status: done
depends: TASK-359
---

# TASK-360 — Backend: Block 9 pre-launch audit — Customers / Notifications / Schedules

Block 9 of the pre-launch audit (`eager-pondering-tower.md`). Modules had zero test coverage
going in.

## Found and fixed

**P0 (worker/notification pipeline entirely broken, both dev and would-be staging/prod):**

1. `worker/src/jobs/notification.job.ts` queried `FROM catalog_products cp JOIN stores s` /
   `FROM stores` — both renamed away (`items`/`locations`, V4 migrations 2026-06-15/16). Every
   expiry warning/critical/expired Telegram/push/email notification has silently thrown
   "relation does not exist" since those migrations — same bug class TASK-358 found in
   `ai-order.job.ts`/`weather-fetch.job.ts`. Fixed both queries.
2. `worker/src/jobs/expiry-check.job.ts` queried `"StoreId"` on `product_stock` — the real
   column is `"LocationId"` (C# `ProductStock.StoreId` maps via Fluent config, but raw SQL here
   wasn't updated). This is the hourly cron described in v1-spec §2.2 — it crashed on its very
   first query every single run, so `product_stock.Status`/`NotifiedWarningAt`/
   `NotifiedCriticalAt` were never touched by time alone and no expiry-alert job was ever
   enqueued. Fixed the column name.
3. Same `"StoreId"` bug in `worker/src/jobs/stock-snapshot.job.ts`'s `product_stock` query (feeds
   the Analytics dashboard's expiry-summary comparison) — fixed alongside #2 since it's the same
   table/column.
4. **Root cause enabler:** local dev `docker-compose.yml`'s worker `DATABASE_URL` was still the
   .NET-format connection string (`Host=...;Database=...`), which `pg`'s `Pool` can't parse —
   every worker DB job failed with `getaddrinfo ENOTFOUND base` on every run. TASK-033
   (2026-06-11) diagnosed and fixed this exact bug for staging/production
   (`WORKER_DATABASE_URL`, postgres:// format) but never applied it to local dev — so bugs #1-3
   (introduced by the mid-June renames, *after* TASK-033) went completely unverified in dev this
   whole time; nothing in this audit series could have live-tested worker behavior until this
   fix. Changed dev's `DATABASE_URL` to `postgresql://crm:crm_dev_password@postgres:5432/crm`.
5. **P1, same file as #2:** `expiry-check.job.ts`'s status thresholds were hardcoded
   (critical ≤1 day, warning ≤3 days) regardless of item, both contradicting v1-spec §2.2
   (7-14d warning / 1-6d critical) and diverging from the backend's own live per-read
   `StockStatus.Compute` (6/14 days default, per-`PerishabilityClass` via
   `PerishabilityClass.GetThresholds` — fresh/chilled/standard/durable). Batches with 4-14 days
   left were cron-invisible: never flagged "warning", so no notification ever fired, even though
   the UI already showed them as warning via the live recompute. TASK-033's own log flagged this
   mismatch as "expected" in 2026-06-11, but `StockStatus.cs` already existed a day earlier
   (2026-06-10) with the correct spec-matching thresholds — the "expected" call was made without
   cross-checking the backend. Fixed: `expiry-check.job.ts` now joins `items` for
   `PerishabilityClass` and mirrors `PerishabilityClass.GetThresholds` exactly.

All three worker fixes live-verified end-to-end after rebuilding the container: manually
triggered `expiry-check` → `notification-alert` → `notification_queue` rows written (25 batches
processed, 18 updated/notified, 0 errors, email/push gracefully `skipped` per TASK-033's
documented no-token behavior) and `stock-snapshot` → 2 real snapshot rows written with correct
counts. Not fixed here (same bug class, different files, Block 11 IoT/Weather scope — flagged as
KI-016 + a background task): `weather-fetch.job.ts`'s `INSERT INTO weather_data ("StoreId", ...)`
still has the same bug (TASK-358 only fixed that file's `SELECT`, not this `INSERT`), and
`mqtt-listener.ts` has several more `"StoreId"` references against IoT tables not yet
individually verified.

**P0 (RLS cross-tenant leak, `notification_settings`):**

Block 2 (TASK-352, `20260714180000_FixFailOpenTenantIsolationOnReset`) deliberately left a
session-level fail-open branch on `notification_settings`'s `tenant_isolation` policy, grouping
it with `users`/`refresh_tokens` as a "legitimate pre-auth lookup" — same reasoning as the
token-refresh flow. That reasoning doesn't actually hold for this table: every access is
`NotificationsController.GetSettings`/`UpsertSetting`, both `[Authorize]`, both resolve `UserId`
from an already-validated JWT — `TenantConnectionInterceptor` has already `SET app.tenant_id`
from the JWT claim by the time either handler runs (only genuinely-anonymous requests get
`RESET`). There is no anonymous code path that touches this table, unlike `refresh_tokens`
(looked up by opaque token before the caller's tenant is known) or `users` (looked up by email
at login).

Live-reproduced with the real non-superuser role (`shelfguard_app`) + `RESET app.tenant_id;
RESET app.role;`: seeded one row each for users in two different tenants, both were visible in a
single unfiltered `SELECT` — the table happened to be empty at Block 2's audit time, which is
why it went unnoticed. Fixed via new migration
`20260715120000_FixNotificationSettingsRlsFailOpen` (removes only the outer session-level
fail-open branch; keeps the inner `OR u."TenantId" IS NULL` — provider accounts genuinely have
`TenantId IS NULL` and get the null-uuid sentinel set, not `RESET`, so they still need it to
manage their own settings). Applied to dev DB, re-verified: RESET state → 0 rows, correctly-
scoped tenant → exactly its own row. Updated
`RlsCrossTenantIntegrationTests.TenantIsolationPolicies_HaveNoFailOpenBranch_ExceptDocumentedPreAuthLookups`'s
allowlist (removed `notification_settings`) and added a dedicated regression test
(`NotificationSettings_FullyResetSession_ReturnsZeroRows_NotEveryTenant`) that hits real
Postgres — both pass.

**P1 (Schedules — shift overlap validation gap):**

`DetectShiftConflicts` (double-booking guard) only ran inside `UpdateScheduleAsync` when a
schedule transitions to `published`. `AddShiftAsync`/`UpdateShiftAsync` never re-checked overlap
at all — adding a shift to an already-published schedule, or editing an existing shift's time
window, could silently double-book an employee regardless of schedule status. Fixed: both
methods now query the user's shifts for that day (`GetShiftsByUserAsync`) and reject an
overlapping, non-cancelled shift (excluding the shift being edited, on update); cancelling a
shift skips the check (removing from the schedule, not placing into it).

**P2 (Customers — no contact-info format validation):**

`CreateAsync`/`UpdateAsync` only checked `Name` non-empty + phone uniqueness — `Phone`/`Email`
had zero format validation (any string accepted). Added `ValidateContactInfo` with a
permissive phone regex (`+`, digits, spaces, dashes, parens, 7-20 chars — deliberately loose,
not E.164-strict, since customers may be entered in varied regional formats) and a standard
email shape check.

## Reviewed and confirmed correct, no changes

- `customers`/`schedule_shifts`/`work_schedules` RLS: confirmed already fixed by Block 2
  (canonical NULLIF pattern, no fail-open branch, `provider_bypass`+`worker_bypass` present) —
  live-checked `pg_policies` directly, not just migration text.
- Indexes: all three modules already have `TenantId`-leading composite indexes matching their
  actual query filters (`idx_customers_email`/`idx_customers_phone`/`idx_customers_tenant`,
  `idx_schedule_shifts_date`/`idx_schedule_shifts_user_date`,
  `idx_notification_queue_tenant_*` ×6). No gaps found, no new indexes needed.
- No N+1 in any list endpoint across the three modules (`CustomerRepository`,
  `ScheduleRepository`, `NotificationRepository` — all single queries with `.Include()` where
  needed).
- Schedules role gating: `SchedulesManageOrCapability` (store_manager+ or `schedules.manage`
  capability) on create/update/delete; plain `[Authorize]` (any tenant role) on read — matches
  v1-spec §3.2's "management by store_manager+, view by all staff" pattern.
- `notification_queue`'s own `OR "TenantId" IS NULL` branch (kept by Block 2, Group B) is a
  different, lower-risk shape than the fail-open bug fixed above — it only exposes rows whose
  own `TenantId` column is null, and no writer in the codebase (backend services or worker jobs)
  ever leaves it null; left as-is, not re-litigated.

## Not fixed (out of scope / needs follow-up)

- KI-016 (`known-issues.md`): `weather-fetch.job.ts`/`mqtt-listener.ts` same `"StoreId"` bug
  class, Block 11 scope — background task spawned.
- KI-017: `needs_verification` status (v1-spec §2.2, 90-day check) has no cron-triggered
  notification at all — needs a schema migration (new notified-at column) + new payload/handler,
  deliberately left out of this task's scope (crash fixes + threshold alignment only).

## Tests

15 new: `CustomerServiceTests` (6 — tenant-id stamping, format validation ×2, existing
duplicate-phone behavior unaffected), `ScheduleServiceTests` (5 — overlap reject/accept on both
add and update, cancel skips the check), `NotificationServiceTests` (3 — channel validation,
tenant-id-never-null on enqueue, event-type validation on settings upsert), plus 1 new
Postgres-integration RLS regression test (`NotificationSettings_FullyResetSession_...`).

`dotnet build` 0 err/0 warn (pre-existing unrelated warning in `MarketplaceServiceTests.cs`
untouched). `dotnet test` 868/868 green (was 846). Worker `npx tsc --noEmit` clean. All new
Postgres-backed RLS tests confirmed actually executing against real Postgres (not soft-skipped —
81-145ms each, not near-instant).

## Migrations

`20260715120000_FixNotificationSettingsRlsFailOpen` — applied to dev DB.

Production/staging **not touched** — same deferral as every prior block of this audit; user
decides when to deploy.
