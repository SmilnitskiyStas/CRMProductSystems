# TASK-419: PriceSegmentSettings schema (Фаза 2 marketing analytics — price segments + frequency decline)

**Agent:** database-engineer
**Date:** 2026-07-27
**Status:** done — created, migrated, and live-verified against the real (non-superuser) app role. No blocker.

## Context

Фаза 2 of `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` (§"Фази 2-4", price segments +
frequency/reactivation) — schema-only slice, direct analogy to `LoyaltyProgramSettings` (TASK-404).
One new tenant-settings table; per the brief, segments/audiences/customer metrics themselves stay
computed live on every request — nothing else persisted.

## Done

- `backend/ShelfGuard.Domain/Entities/PriceSegmentSettings.cs` — exact shape from the brief
  (`Id`/`TenantId`, `DefaultFrequencyDeclineThresholdPercent` default `30.0m`, nullable
  `MinReceiptsForBoundaries`, `UpdatedAt`, `Tenant?` nav). Used English `///` XML doc comments
  (translating the brief's Ukrainian inline `//` comments' meaning) to match
  `LoyaltyProgramSettings.cs`'s own style exactly — that sibling file is fully English despite the
  surrounding plan doc being Ukrainian.
- `AppDbContext.cs` — `DbSet<PriceSegmentSettings>` + fluent config block mirroring the
  `LoyaltyProgramSettings` block exactly: `ToTable("price_segment_settings")`, unique index on
  `TenantId` (`uq_price_segment_settings_tenant`), FK → `tenants` `Restrict`. `Tenant.cs` untouched
  (no inverse nav property — same one-way `HasOne(...).WithMany()` pattern every sibling settings
  table uses).
- Migration `AddPriceSegmentSettings` (`20260726211248`) — canonical RLS triad only
  (`tenant_isolation` NULLIF-guarded + `provider_bypass` + `worker_bypass`), deliberately **no**
  `consumer_self_access` — staff-only, no consumer read path to this table at all, same posture as
  `loyalty_program_settings` itself. Confirmed (per brief's ask) that `provider_bypass` still means
  `IN ('provider', 'provider_admin')` — TASK-404's `AddLoyaltyProgram` already made this exact call
  for its three tenant-scoped tables (referencing `20260714150000_ExpandProviderBypassToProviderAdmin`),
  and it's unaffected/still the applied convention — used the identical `IN (...)` form here rather
  than the older single-role text in `database-engineer.md`'s literal template.

## Operational lesson from TASK-411 — not repeated

Applied via the app's own **non-superuser** connection (`shelfguard_app_dev`) first, not the `crm`
superuser escape hatch. It applied cleanly — no FK-validation-under-RLS false-positive `23503` (as
anticipated: a brand-new empty FK column, not an already-populated one referencing an RLS parent,
so the documented gotcha's precondition never applied). Confirmed ownership immediately after,
before assuming success: `SELECT tablename, tableowner FROM pg_tables WHERE tablename =
'price_segment_settings'` → `shelfguard_app_dev`. No `FixLoyaltyTableGrants`-style companion
migration was needed — TASK-411's incident does not recur here.

## Verification (live, real app role, not superuser)

Everything below ran through `docker exec ... psql -U shelfguard_app_dev` (the actual
`DefaultConnection` role from `appsettings.Development.json`), never `crm`:

1. **Positive path**, one RLS-scoped transaction (`SET LOCAL app.tenant_id = <real tenant>`,
   `app.role = 'enterprise_admin'`): INSERT → SELECT → UPDATE all succeeded, then `ROLLBACK` (no
   residue left).
2. **Fail-closed**: no `app.tenant_id` set at all → `SELECT count(*)` returns `0` (not an error, not
   every row); INSERT rejected with `new row violates row-level security policy`.
3. **Cross-tenant isolation**: a row committed under tenant A, read back under a genuinely different
   tenant B's session → `0` visible.
4. **Bypass roles**: `provider`, `provider_admin`, and `worker` sessions (no `tenant_id` set) each see
   the row (`1`); the committed row was then deleted under its own tenant and re-confirmed `0` rows
   remain anywhere (checked via `provider` bypass) — dev DB left clean.
5. **Policy/flag byte-check** (`pg_policies`/`pg_class`): `relrowsecurity`/`relforcerowsecurity` both
   `t`; exactly 3 policies present (`tenant_isolation`, `provider_bypass`, `worker_bypass`) with the
   intended `qual` text (`provider_bypass`'s qual is literally
   `current_setting('app.role', true) = ANY (ARRAY['provider','provider_admin'])`); FK
   `ON DELETE RESTRICT` to `tenants`; unique btree index on `TenantId`.
6. `dotnet build` — 0 err (1 pre-existing unrelated warning in `MarketplaceServiceTests.cs`, same one
   TASK-404/411/414 all reported). `dotnet test` — **1109/1109 green**, unchanged from TASK-417's
   baseline — no regressions.

## No new xUnit test file (deliberate)

This table's RLS shape (canonical triad, no identity-based policy) is already covered by two
existing **dynamic** audits in `RlsCrossTenantIntegrationTests.cs` that enumerate every FORCE-RLS
table at query time rather than naming tables by hand:
`AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass` and
`TenantIsolationPolicies_HaveNoFailOpenBranch_ExceptDocumentedPreAuthLookups`. Both already re-ran
green against `price_segment_settings` as part of the 1109/1109 pass above, with zero new test code
required — same reasoning TASK-404 used to justify a genuinely *new* test file only for its novel
`consumer_self_access` policy shape, which this table intentionally does not have.

## Not in scope (per brief, unchanged)

- No new tables for segments/audiences/customer metrics — all computed live, per the brief.
- No module-key change — `"marketing_analytics"` has been registered in `Tenant.UpdateModules` since
  TASK-405/406; this table rides under that existing key.
- `.claude/docs/database-schema.md` not updated — same precedent TASK-404 set (a documentation-writer
  pass, e.g. TASK-415 for loyalty/RFM, covers schema docs for this plan; this task's brief also
  scoped it to "only this one table, no other changes").

## Git

Not committed — working tree left for review (repo convention: main session/user commits).

## Files

- `backend/ShelfGuard.Domain/Entities/PriceSegmentSettings.cs` (new)
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` (DbSet + fluent config added)
- `backend/ShelfGuard.Infrastructure/Migrations/20260726211248_AddPriceSegmentSettings.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260726211248_AddPriceSegmentSettings.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (regenerated by
  `dotnet ef migrations add`, `PriceSegmentSettings` metadata only)
