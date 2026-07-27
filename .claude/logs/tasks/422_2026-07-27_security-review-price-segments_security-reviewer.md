# TASK-422: Security review — Фаза 2 (price segments + frequency/reactivation)

**Agent:** security-reviewer
**Date:** 2026-07-27
**Status:** done — **verdict: CLEAR TO SHIP.** No blocker, no risk-level finding. 2 minor
non-security notes for a future task.

## Context

Read TASK-419 (database-engineer, `PriceSegmentSettings` schema)/420 (backend-developer, engine +
API)/421 (frontend-developer, UI) logs first, then read the actual code directly — did not take
any of the three logs' security claims on trust; independently re-derived each of the 6 mandatory
checklist items from the source, plus a general pass over the rest of the diff.

## Verdict by checklist item

**1. Raw-SQL parameterization + `sortBy` allowlist (`PriceSegmentsRepository.cs`) — OK.**
Traced the full flow end-to-end for all 3 paginated queries (`GetPriceAudienceTableAsync`/
`GetAllTimeCustomerTableAsync`/`GetFrequencyAudienceTableAsync`): controller → service → repository
never lets the raw `sortBy` string reach SQL text. `PriceSegmentSortKeys.Normalize*` lowercases,
trims, and checks membership against a fixed `HashSet<string>` allowlist; an unrecognized value
silently falls back to a hardcoded default (never a 400, never passthrough). The repository's own
`PriceAudienceSortColumn`/`AllTimeSortColumn`/`FrequencySortColumn` then switch on that *normalized*
key to one of a small set of HARDCODED literal column names — only that literal (never the caller's
string) gets `string.Replace`'d into the SQL template at `{SORT_COLUMN}`/`{SORT_DIRECTION}`, using a
token syntax deliberately distinct from EF's own `{0}..{n}` positional placeholders so the two
substitution mechanisms can't collide. Verified each mapped literal actually exists as a column in
that query's final `filtered`/`classified` CTE (no silent runtime-error risk either). `sortDescending`
is a plain `bool` from model binding — only 2 possible literal outputs (`"ASC"`/`"DESC"`), no surface
at all. Every other filter (tenantId, storeIds, dates, segment/audience ordinals, spend thresholds,
declineThresholdPercent, page/offset) is a genuine positional `{n}` `SqlQueryRaw` parameter, real
Npgsql parameters, confirmed by reading every one of the 4 SQL-building methods — zero string
interpolation of user input anywhere in this file. Nullable value-type params go through a
`NullableParam<T>` helper boxing to `DBNull.Value`, not a bare null — correctly typed, not a second
injection surface. No fix needed.

**2. RLS on `price_segment_settings` — OK.**
Read migration `20260726211248_AddPriceSegmentSettings.cs` directly and diffed it, line for line,
against the sibling `loyalty_program_settings` migration (`20260726132332_AddLoyaltyProgram.cs`) —
**byte-for-byte identical RLS block modulo the table name**: `ENABLE`+`FORCE ROW LEVEL SECURITY`,
`tenant_isolation` NULLIF-guarded fail-closed (`current_setting('app.tenant_id', true)` → NULLIF →
cast; no session var set means the equality can never be true, never fail-open), `provider_bypass`
as `IN ('provider','provider_admin')` (matches the post-`ExpandProviderBypassToProviderAdmin`
convention), `worker_bypass` = `'worker'`. Confirmed no `consumer_self_access` — correct, this table
has no consumer-facing read path (same posture as `loyalty_program_settings` itself). TASK-419's own
log documents live verification (positive path, fail-closed, cross-tenant isolation, bypass roles,
policy byte-check) against the real non-superuser app role, not just `crm` — no reason to doubt it,
and the static text independently confirms the same shape. No fix needed.

**3. PII in exports — OK.**
All three new export builders (`BuildPriceAudienceExcel`/`BuildAllTimeExcel`/`BuildFrequencyExcel` in
`PriceSegmentsService.cs`) route every cell through `_excel.Export(new ExcelExportRequest(...))` →
`ExcelExportService.Export` → `SetCellValue`, which passes every string (explicit `string` case AND
the `.ToString()` fallback for every other type) through `SanitizeForSpreadsheet` — the exact
TASK-414 formula-injection guard (leading `=`/`+`/`-`/`@`/Tab/CR gets apostrophe-prefixed, verified
in that file to still be the one, only choke point; no new cell-writing path bypasses it). Phone is
masked by default via `PiiMasking.MaskPhone` (moved verbatim from `MarketingAnalyticsService`, not
forked — confirmed identical logic) unless `unmaskPii` is true. Server-side re-derivation is present
on all 3 export actions in `PriceSegmentsController.cs`:
`request with { UnmaskPii = request.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User) }`
— a client sending `unmaskPii: true` without the role/capability gets silently clamped back to
masked, never trusted standalone. Phase 2's customer queries only ever `SELECT` `Name`/`Phone` from
`customers` (confirmed in the repository's SQL — no `Email` column touched anywhere in this
feature), so there is no email-masking gap to check for this phase (unlike Фаза 1, which does export
email and needed the TASK-414 fix). No fix needed.

**4. Capability-gate reuse — OK.**
`PriceSegmentsController` carries both `[Authorize(Policy = AppPolicies.MarketingAnalyticsViewOrCapability)]`
and `[RequireModule("marketing_analytics")]` at class level (read directly, applies to every action,
no per-action override anywhere in the file) — literally the same policy constant and module key as
`MarketingAnalyticsController`, not a re-declared duplicate. `PriceSegmentSettingsController` uses
`[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]` with no `[RequireModule]` attribute —
checked this isn't an omission by comparing directly against the precedent
`LoyaltySettingsController`, which has the exact same shape (`AtLeastEnterpriseAdmin` only, no
`RequireModule`); this is the established convention for tenant-settings controllers in this
codebase, not a gap specific to this task. No fix needed.

**5. `PriceSegmentSettingsController` threshold validation — OK.**
`PriceSegmentsService.UpsertSettingsAsync` rejects before touching the repository:
`DefaultFrequencyDeclineThresholdPercent < 0 || > 100` → 400 via `(null, "...must be between 0 and
100.")`, and `MinReceiptsForBoundaries is < 0` → 400. Controller maps a non-null error to
`BadRequest(new { error })`. Confirmed by reading the method body directly (not inferred from the
DTO shape) — a caller cannot persist a threshold that would silently corrupt the frequency-decline
percentage math. No fix needed.

**6. `PERCENTILE_CONT`/`::numeric` cast consistency — OK.**
Grepped the whole backend for `PERCENTILE_CONT`; all 15 call sites live in
`PriceSegmentsRepository.cs` (matches TASK-420's own count) and every single one — per-customer
median check, boundaries P20/P40/P60/P80/P90/P97, all-time KPI/distribution/monthly-trend median,
both comparison-table cur/prev CTEs, both frequency-table cur/prev CTEs — is wrapped in
`(...)::numeric` immediately at the point of computation; none missing. Фаза 1's
`MarketingAnalyticsRepository.cs` doesn't use `PERCENTILE_CONT` at all (different quantile approach),
so there's no cross-file drift risk either. No fix needed.

## Additional checks made beyond the 6-item list (nothing blocking found)

- **DI lifetimes:** `IPriceSegmentsService`/`IPriceSegmentsRepository`/`IPriceSegmentAdvisor` all
  registered `AddScoped` in both `DependencyInjection.cs` files — no scoped-`AppDbContext`-captured-
  by-singleton bug class.
- **Claude key resolution (`PriceSegmentAdvisor.ResolveAsync`):** queries `IntegrationConfigs`
  without an explicit `TenantId` filter, relying on RLS alone for tenant scoping — confirmed this is
  a byte-for-byte copy of the already-shipped `MarketingAdvisor.ResolveAsync` (same query, same
  reliance), not a new pattern introduced here; not a new risk.
- **Page-size ceiling:** the controller's own `NormalizePageSize` has no upper bound, but every one
  of the 3 paginated service methods re-normalizes via `PriceSegmentsService.NormalizePaging`, whose
  `MaxPageSize = 200` is what actually reaches the SQL `LIMIT`/`OFFSET` — verified this re-clamp is
  applied on all 3 paginated code paths, so there is no unbounded-`LIMIT` DoS vector via a huge
  `pageSize` query param. Style nit only (two normalization functions with different rules stacked),
  not a defect.
- **Export row cap:** export request DTOs (`ExportPriceAudienceRequest`/`ExportAllTimeRequest`/
  `ExportFrequencyAudienceRequest`) carry no page/pageSize field at all — exports always call the
  repository with `(1, ExportMaxRows)` hardcoded (50,000), so a client cannot request an unbounded
  export.
- **Enum route/query params** (`PriceAudienceKey`/`PriceSegmentKey`/`FrequencyAudienceKey`): an
  out-of-range integer still binds (ASP.NET Core's default enum binder doesn't restrict to defined
  values), but it only ever reaches SQL as a bound positional parameter and simply matches zero rows
  in the CASE ladder — not an injection vector, not a crash (`PriceSegmentCatalog.FromOrdinal`'s
  throw path is only reached for a `SegmentOrdinal` that is guaranteed 1-7 by the SQL CASE's own
  `ELSE 7`, never by the raw enum route value).
- **Cross-tenant store-filter check:** `storeIds` arrays are always ANDed with `t."TenantId" = {0}`
  in every query — even if a caller passes a `storeId` belonging to a different tenant, the tenant
  predicate (sourced from the JWT claim, not request input) means it can only ever narrow results
  within the caller's own tenant, never leak another tenant's rows.

## Non-security observations (not part of this review's scope, flagged for a separate task)

- `PriceSegmentSettings.MinReceiptsForBoundaries` is validated, persisted, and returned by the
  Settings CRUD, but is never actually read by `GetBoundariesAsync` or any other repository method —
  the setting currently has no effect on the boundary calculation. Functional/product gap, not a
  security issue; worth a follow-up ticket so the setting isn't silently inert.

## Not done (out of scope for this review)

Did not re-run `dotnet build`/`dotnet test` myself — this was a read-only code review, and TASK-420's
log already reports 1180/1180 green including live-Postgres RLS/integration coverage for the exact
paths reviewed here. No code changed by this task.

## Files reviewed (no changes made)

- `backend/ShelfGuard.Infrastructure/Data/Repositories/PriceSegmentsRepository.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/20260726211248_AddPriceSegmentSettings.cs`
- `backend/ShelfGuard.Api/Controllers/PriceSegmentsController.cs`
- `backend/ShelfGuard.Api/Controllers/PriceSegmentSettingsController.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/PriceSegments/PriceSegmentsService.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/PriceSegments/PriceSegmentSortKeys.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/PriceSegments/PriceSegmentCatalog.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/PriceSegments/IPriceSegmentsRepository.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/PriceSegments/Dtos/PriceSegmentDtos.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/PriceSegments/Dtos/FrequencyDtos.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/PiiMasking.cs`
- `backend/ShelfGuard.Infrastructure/Export/ExcelExportService.cs`
- `backend/ShelfGuard.Infrastructure/Authorization/AppPolicies.cs`
- `backend/ShelfGuard.Infrastructure/Authorization/MarketingAnalyticsAuthorization.cs`
- `backend/ShelfGuard.Domain/Constants/TenantRoleCapabilities.cs`
- `backend/ShelfGuard.Infrastructure/AI/PriceSegmentAdvisor/PriceSegmentAdvisor.cs`
- `backend/ShelfGuard.Api/Controllers/LoyaltySettingsController.cs` (precedent comparison)
- `backend/ShelfGuard.Infrastructure/Migrations/20260726132332_AddLoyaltyProgram.cs` (precedent
  comparison)
- `backend/ShelfGuard.Application/DependencyInjection.cs`, `backend/ShelfGuard.Infrastructure/DependencyInjection.cs`

## Git

Not committed — review only, no code changes.
