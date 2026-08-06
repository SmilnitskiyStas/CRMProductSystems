# TASK-471: Post-campaign segment schema (Фаза 4 marketing analytics)

**Agent:** database-engineer
**Date:** 2026-08-05
**Status:** done — created, migrated, and live-verified against the real (non-superuser) app role. No blocker.

## Context

Фаза 4 of `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` (§"Фази 2-4", post-campaign
audience analysis) — schema-only slice. Full spec source:
`docs/uployal/AUDIENCE_ANALYSIS.md`. Unlike Фаза 1-3 (fully stateless, computed live from
`pos_transactions`/`items`/`customers` on every request), Фаза 4 persists an uploaded customer-id
list, its import-validation results, and the frozen before/after date windows — the source doc's
own §7 ("Чернетка та застосований сегмент") requires draft vs. analyzed to be two distinct,
explicitly-tracked states.

## Done

- `backend/ShelfGuard.Domain/Entities/PostCampaignSegment.cs` (new) — header row per uploaded/
  analyzed audience: `Id`/`TenantId`/`CreatedByUserId`, optional `Name`, five import-result
  counters (`UploadedCount`/`MatchedCount`/`DuplicateCount`/`UnknownCount`/`InvalidCount`),
  `UnknownTokensSample`/`InvalidTokensSample` (`List<string>`, capped ~20 by the service layer,
  not enforced in schema), `AfterStart`/`AfterEnd`/`BeforeStart`/`BeforeEnd` (`DateOnly?` —
  nullability IS the draft-vs-analyzed state), `SegmentHash`, `CreatedAt`/`AnalyzedAt`.
- `backend/ShelfGuard.Domain/Entities/PostCampaignSegmentMember.cs` (new) — one row per matched
  customer: `Id`/`TenantId`/`SegmentId`/`CustomerId`. Unknown/invalid tokens are never
  materialized here, only counted + sampled on the parent.
- `AppDbContext.cs` — two new DbSets + fluent config blocks, mirroring `LoyaltyMembership`/
  `LoyaltyLedgerEntry` (TASK-404) and `PriceSegmentSettings` (TASK-419) conventions:
  - `PostCampaignSegment` is the top-level tenant-scoped table — real `TenantId -> tenants` FK
    (Restrict), `CreatedByUserId -> users` FK **Restrict** (non-nullable; mirrors
    `UserPermissionGrant.GrantedByUserId`, the existing precedent for a required, non-nullable
    "staff-authored row references its author" FK — most `CreatedByUserId`/`CreatedBy` columns
    in this codebase are nullable+SetNull, but a segment always has an owner so this one isn't).
  - `UnknownTokensSample`/`InvalidTokensSample` — `jsonb` + `'[]'::jsonb` default, same exact
    pattern as `Item.Barcodes` (the original KI-013 `List<string>`/JSONB case; `EnableDynamicJson()`
    in `DependencyInjection.cs` already covers this, not re-verified per the brief).
  - `PostCampaignSegmentMember.TenantId` is a plain, indexed, denormalized column with **no**
    separate FK to `tenants` — same treatment `loyalty_ledger_entries.TenantId` already gets
    (TASK-404): the real parent linkage is the FK to `SegmentId`, not a redundant direct FK.
  - `SegmentId -> post_campaign_segments` (Cascade, wired via the parent's `HasMany`) and
    `CustomerId -> customers` (Cascade) per the brief; unique `(SegmentId, CustomerId)`.
- Migration `AddPostCampaignSegmentSchema` (`20260805190701`) — canonical RLS triad only on both
  tables (`tenant_isolation` NULLIF-guarded + `provider_bypass` `IN ('provider','provider_admin')`
  + `worker_bypass`), deliberately **no** `consumer_self_access` — staff-only, same posture as
  `price_segment_settings`. Indexes: `idx_post_campaign_segments_tenant_creator` (TenantId,
  CreatedByUserId), `idx_post_campaign_segment_members_tenant_segment` (TenantId, SegmentId),
  `uq_post_campaign_segment_members_segment_customer` (unique, SegmentId+CustomerId).

## No FK-validation-under-RLS gotcha (KI-029) — confirmed, not just assumed

Both tables are brand-new and empty at `CreateTable` time, so there was no pre-existing data for
Postgres to validate the new FKs against regardless of which role ran the migration — this failure
mode (`23503` false-positive under `FORCE ROW LEVEL SECURITY`) only applies to `ALTER TABLE ...
ADD CONSTRAINT` against an *already-populated* column. Applied directly via the app's own
**non-superuser** `shelfguard_app_dev` connection (`dotnet ef database update` with
`ConnectionStrings__DefaultConnection` pointed explicitly at it — the design-time factory's
fallback otherwise resolves to an unrelated local `postgres` role/port, unrelated to this task) —
it applied cleanly on the first try, no `crm` superuser escape hatch needed, no TASK-411-style
grant incident.

## Verification (live, real app role, not superuser)

Ran via `docker exec -i crmproductsystems-postgres-1 psql -U shelfguard_app_dev -d crm` (the actual
`DefaultConnection` role), never `crm`:

1. **Ownership** — `pg_tables`: both tables owned by `shelfguard_app_dev` (not a bootstrap
   superuser) immediately after migration, before assuming success.
2. **Policy/flag byte-check** — `pg_class`: `relrowsecurity`/`relforcerowsecurity` both `t` on
   both tables. `pg_policies`: exactly 3 policies each (`tenant_isolation`, `provider_bypass`,
   `worker_bypass`), correct `qual` text (NULLIF guard present; `provider_bypass` is
   `= ANY (ARRAY['provider','provider_admin'])`).
3. **Positive path** (tenant-scoped transaction, `SET LOCAL app.tenant_id`/`app.role =
   'enterprise_admin'`): insert segment + member, select both back, update the segment, then
   rolled back — no residue.
4. **Unique backstop**: inserting a second `(SegmentId, CustomerId)` pair correctly raised
   `duplicate key value violates unique constraint "uq_post_campaign_segment_members_segment_customer"`.
5. **Fail-closed**: no `app.tenant_id`/`app.role` set at all → `SELECT count(*)` returns `0` on
   both tables (not an error, not every row); INSERT correctly rejected with `new row violates
   row-level security policy for table "post_campaign_segments"`.
6. **Cross-tenant isolation**: a segment committed under tenant A is visible under tenant A's own
   session (`1`) and invisible under a second, genuinely different tenant B (`0`).
7. **Bypass roles**: `provider`, `provider_admin`, `worker` (no `tenant_id` set) each see the
   cross-tenant test row (`1`).
8. **Cascade**: deleting the parent segment (as `worker`) correctly cascaded away its member row
   (`0` remaining) — confirms `SegmentId -> post_campaign_segments` `Cascade` actually fires.
9. **Cleanup**: all test rows removed, `leftover_segments`/`leftover_members` both `0` — dev DB
   left clean.
10. `dotnet build` — **0 warnings, 0 errors**. `dotnet test` — **1222/1222 green**, unchanged
    baseline from ADR-026/TASK-469 — no regressions.

## No new xUnit test file (deliberate)

Same reasoning as TASK-419: this schema's RLS shape (canonical triad, no identity-based policy)
is already covered by the two existing **dynamic** audits in `RlsCrossTenantIntegrationTests.cs`
that enumerate every FORCE-RLS table at query time —
`AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass` and
`TenantIsolationPolicies_HaveNoFailOpenBranch_ExceptDocumentedPreAuthLookups` — both re-ran green
against the two new tables as part of the 1222/1222 pass above, zero new test code required.

## Not in scope (per brief)

- No service/controller/repository logic — that's TASK-472 (backend-developer), strictly after
  this migration.
- No frontend files.
- `Features/MarketingAnalytics/AudienceBuilder` (Фаза 3) and all other existing Фаза 0-3 code —
  untouched, confirmed via `git status`/`git diff` scoped to only the files listed below.
- No `Tenant.modules` change — `"marketing_analytics"` has been a registered valid module key
  since TASK-405/406 (ADR-023); Фаза 4 rides under that existing key, no new one needed.
- `.claude/docs/database-schema.md` **not updated** — same precedent TASK-404/TASK-419 both set:
  this plan's own agent sequence assigns `.claude/docs/` updates (glossary, database-schema,
  domain-model, api-contracts, ADR) to a dedicated documentation-writer pass once all of Фаза 4
  ships, not to database-engineer per schema-only task. This task's brief also does not list it
  as a deliverable.

## Git

Not committed — working tree left for review (repo convention: main session/user commits).

## Files

- `backend/ShelfGuard.Domain/Entities/PostCampaignSegment.cs` (new)
- `backend/ShelfGuard.Domain/Entities/PostCampaignSegmentMember.cs` (new)
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` (2 DbSets + 2 fluent config blocks added)
- `backend/ShelfGuard.Infrastructure/Migrations/20260805190701_AddPostCampaignSegmentSchema.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260805190701_AddPostCampaignSegmentSchema.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (regenerated by
  `dotnet ef migrations add`, `PostCampaignSegment`/`PostCampaignSegmentMember` metadata only)
