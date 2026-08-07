# TASK-482: Single-product sales trend endpoint

**Agent:** backend-developer
**Date:** 2026-08-07
**Status:** done

## Context

Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md` (interactive analytics + margin
initiative). Last backend task in the chain — depends on TASK-479 (DB index, done), TASK-480
(`CanViewMargin`, done), TASK-481 (category/losses endpoints, done — touched the same 3 files,
read fresh before editing, nothing of theirs modified). Blocks TASK-484 (frontend, product trend UI).

## Done

**`GET /api/analytics/pos/products/{productId}/trend`** on `AnalyticsController.cs`, same
class-level `AnalyticsViewOrCapability` policy, same `ResolveTenantId()`/`IsProvider()`/
`ResolveDateRange()` conventions as every other action. No compare-mode variant (row-click
drill-down, not a page-level KPI trend). 404s (`NotFound()`, no body) when `productId` doesn't
resolve to a real `Item` in the caller's tenant scope — mirrors `ItemsController.GetById`'s
nullable-DTO convention exactly: repository returns `null`, service pass-through returns `null`,
controller does `result is null ? NotFound() : Ok(result)`.

New DTOs in `PosAnalyticsDtos.cs` (not `AnalyticsDtos.cs` — that's TASK-481's file, untouched):
`ProductSalesTrendDto`/`ProductSalesTrendPointDto`, exact shapes from the brief. Thin
`IAnalyticsService`/`AnalyticsService.cs` pass-through. New repo method
`GetProductSalesTrendAsync` in `AnalyticsRepository.cs`: resolves the `Item` (tenant-scoped,
belt-and-suspenders explicit `tenantId` filter on top of RLS, matching every other method in this
file) for `ProductName`/`PricePurchase`/the-404-check in one query, then groups
`PosTransactionItems` (filtered by `ProductId`, joined to `BuildPosTransactionQuery`'s tenant/
store/date-filtered subquery) **in SQL before `ToListAsync`** — deliberately not the
`GetPosTopProductsAsync` in-memory-materialize-then-group anti-pattern. Margin (ADR-027) is a
cheap second pass over the already-collapsed (≤366-row) points list, `Revenue − Quantity ×
Item.PricePurchase`, gated by `includeMargin` (controller resolves via
`AnalyticsAuthorization.CanViewMargin(User)`, same call shape as TASK-481).

### Deviation from the plan's literal query snippet — `EF.Functions.DateTrunc` doesn't exist

The plan brief's C# snippet assumed `EF.Functions.DateTrunc(groupBy, x.CreatedAt)` as the GroupBy
key. **That method does not exist in this repo's installed
`Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.11** — confirmed two ways: `dotnet build` fails with
CS1061, and grepping the package's own XML doc file for every `Npgsql*DbFunctionsExtensions`
member (ILike, JsonContains, full-text, network/inet, trigrams, fuzzy-match, statistical
aggregates — no date truncation at all). Root-caused and fixed rather than guessing:
- **"day"** uses the provider's built-in `DateTime.Date` member translation, confirmed via
  `.ToQueryString()` against a real `AppDbContext` pointed at local dev Postgres:
  `date_trunc('day', "CreatedAt", 'UTC')` — Npgsql already passes an explicit `'UTC'` third
  argument, so this is timezone-safe regardless of session `TimeZone` (also independently
  confirmed the dev server's own session `TimeZone` is `UTC`).
- **"week"** inlines the same Monday-anchored ISO-week arithmetic `IsoWeekStart()` already uses
  below (`dow == 0 ? -6 : 1 - dow`) as translatable `DateTime` member arithmetic
  (`x.CreatedAt.Date.AddDays((int)x.CreatedAt.DayOfWeek == 0 ? -6 : 1 - (int)x.CreatedAt.DayOfWeek)`)
  instead of calling that private method directly (EF cannot translate calls to arbitrary C#
  methods). Confirmed translatable the same way.
- Implemented as two full query branches (`resolvedGroupBy == "week" ? await ... : await ...`)
  rather than one query with a runtime-conditional GroupBy key, to avoid an untested third
  translation shape — both branches share the same join/filter base query and produce
  identically-shaped anonymous projections, so they unify under one `grouped` variable without
  duplicating the margin/DTO-mapping pass that follows.

### Live verification against real data (not deferred, unlike TASK-479)

Local dev Postgres turned out to have real data now (residue from TASK-476's QA E2E pass — 131
`pos_transactions`, 254 `pos_transaction_items`, confirmed before relying on it). Rather than
defer per TASK-479's precedent, ran the actual generated SQL for both branches via
`EXPLAIN ANALYZE` as the app's own non-superuser `shelfguard_app_dev` role (RLS-bound, real
`app.tenant_id` session var — not the `crm` superuser, which would bypass RLS and give a
misleading plan) against a real product (47 sale rows, tenant `8abfbbb5-…`, Feb–Jul 2026 spread):
- **Day and week grouping both hit `Index Only Scan using idx_pos_transaction_items_product_covering`, `Heap Fetches: 0`** — TASK-479's index is used exactly as designed, not bypassed.
- Day-bucketed points summed (revenue/qty/count) **exactly** matched an independently-computed
  ground-truth aggregate over the same 47 rows — no double-counting or dropped rows from the
  join/group.
- Every week-bucket key, spot-checked with `to_char(..., 'Day')`, landed precisely on **Monday**
  — confirms the inlined ISO-week arithmetic is correct, not just translatable.
- Execution time ~3ms either way at this data volume.

(Also surfaced, incidentally, that this table's RLS has a second ANDed "store-scope" layer beyond
simple tenant isolation — gated on `app.user_id` / `user_locations`, live since the Stage 3
rollout — orthogonal to this task, not touched, noted here only because it shaped how the
verification query needed `app.role` set to a role that's exempted from that layer.)

Verification was done via a temporary scratch test file
(`ShelfGuard.Tests/Analytics/_Scratch482Verify.cs`, `.ToQueryString()` against several GroupBy-key
candidates) and scratch SQL in the session scratchpad — both deleted/not part of this diff; only
the findings and the final query shape are retained here and in code comments.

## Tests

Added 4 facts to `PosAnalyticsServiceTests.cs` (TASK-481's file, read fresh, nothing of theirs
changed): day/week `groupBy` pass-through (mirrors the existing `GetPosRevenueTrendAsync` pair),
the margin-role test (same `ClaimsPrincipal`/`CanViewMargin` shape as TASK-481's), and
null-propagation on unknown `productId` (repo returns null → service returns null — this
codebase has no `*ControllerTests.cs` anywhere, so the controller's one-line
`NotFound()`-ternary itself isn't independently unit-tested, consistent with how
`ItemServiceTests.cs` tests the equivalent `GetByIdAsync`-returns-null case one layer down rather
than a controller layer that doesn't exist here).

## Build/test

- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing unrelated warning,
  `MarketplaceServiceTests.cs`).
- `dotnet test ShelfGuard.sln` — 1333/1333 green (1329 baseline + 4 new), no regressions.
- `EXPLAIN ANALYZE` — done against real data, see above (index used, results correct).

## Not in scope (per brief)

No changes to `AnalyticsDtos.cs`, `CategoryProductBreakdownDto`/`LossesByProductDto`/their
endpoints (TASK-481), `AnalyticsAuthorization.cs`/`TenantRoleCapabilities.cs` (TASK-480, only
consumed `CanViewMargin`), the TASK-479 migration/index, or anything under `frontend/`.

## Files

- `backend/ShelfGuard.Api/Controllers/AnalyticsController.cs`
- `backend/ShelfGuard.Application/Features/Analytics/Dtos/PosAnalyticsDtos.cs`
- `backend/ShelfGuard.Application/Features/Analytics/IAnalyticsRepository.cs`
- `backend/ShelfGuard.Application/Features/Analytics/IAnalyticsService.cs`
- `backend/ShelfGuard.Application/Features/Analytics/AnalyticsService.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/AnalyticsRepository.cs`
- `backend/ShelfGuard.Tests/Analytics/PosAnalyticsServiceTests.cs`

## Route contract (for TASK-484 frontend)

`GET /api/analytics/pos/products/{productId}/trend?store_id&from&to&group_by=day|week`
(all query params optional; `productId` is a required path segment, `Guid`). 200 body
(camelCase, System.Text.Json default — no explicit naming policy configured anywhere in
`ShelfGuard.Api`, confirmed by grep):
```json
{
  "productId": "guid",
  "productName": "string",
  "groupBy": "day",
  "points": [
    { "date": "2026-07-26", "revenue": 513.80, "quantity": 30.00, "transactionCount": 28, "marginAmount": null }
  ]
}
```
`marginAmount` is `null` unless the caller clears `AnalyticsAuthorization.CanViewMargin`
(network_manager+ or the `analytics.view_margin` capability) — server-side null, not omitted or
zeroed, same as TASK-481's DTOs. 404 with an empty body when `productId` doesn't resolve to a
real `Item` for the caller's tenant.
