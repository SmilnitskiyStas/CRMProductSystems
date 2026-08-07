# TASK-480: Margin authorization primitive

**Agent:** backend-developer
**Date:** 2026-08-07
**Status:** done

## Context

Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md` (interactive analytics + margin
initiative). Depends on nothing (TASK-479's index has no code overlap). Blocks TASK-481/482, which
will wire this check into the actual margin-bearing endpoints/DTOs.

## Done

- `backend/ShelfGuard.Infrastructure/Authorization/AnalyticsAuthorization.cs` (new) —
  `CanViewMargin(ClaimsPrincipal)`: `AppPolicies.AtLeastNetworkManagerRoles.Any(user.IsInRole)` OR
  `TenantRoleAuthorization.HasCapability(user, TenantRoleCapabilities.AnalyticsViewMargin)`. Mirrors
  `MarketingAnalyticsAuthorization.CanExportPii`'s shape exactly (imperative in-body check, not a
  policy attribute — narrows two DTO fields within a response, not the whole endpoint).
  `AppPolicies.AtLeastNetworkManagerRoles` already existed (`AppPolicies.cs:111-112`) — confirmed
  by reading the file before use, no new role-set constant needed.
- `backend/ShelfGuard.Domain/Constants/TenantRoleCapabilities.cs` — new
  `AnalyticsViewMargin = "analytics.view_margin"` in the "Бухгалтер / Фінансист" group (next to
  `AnalyticsView`), added to `All` and to the `Groups` catalog with a Ukrainian label.
- `backend/ShelfGuard.Tests/Authorization/AnalyticsAuthorizationTests.cs` (new) — 9 facts mirroring
  `MarketingAnalyticsAuthorizationTests`' `CanExportPii` set, shifted up one role tier: true for
  network_manager/enterprise_admin/provider; false for store_manager/cashier/merchandiser with no
  capability (store_manager is the important negative case — it clears the controller's own
  store_manager+ floor but not this narrower check); true for store_manager and for the lowest role
  rank (staff) via the capability claim; false when the capability claim has only unrelated keys.
- `.claude/docs/decisions.md` — new `ADR-027` (inserted above ADR-026, newest-first convention):
  records the margin cost-source decision (current `Item.PricePurchase` applied retroactively to
  all historical `PosTransactionItem` rows, why exact batch cost isn't reachable — transfer-chain
  and production source types, nullable `ProductStockId` SET NULL on batch delete — the binding
  "оцінна маржа" UI label requirement, and the deferred `CostAtSale` snapshot fast-follow), the two
  deferred `/analytics/pos` interactions (cashier drill-down, payment-type filter) as backlog, and
  a short summary of this task's own authorization primitive (including the KI-030 capability-path
  caveat).

## Build/test

- `dotnet build` — 0 errors (1 pre-existing unrelated warning, `MarketplaceServiceTests.cs`).
- `dotnet test` — 1323/1323 green (1314 baseline + 9 new `AnalyticsAuthorizationTests`), no
  regressions.

## Not in scope (per brief)

No changes to `AnalyticsController.cs`, `AnalyticsService.cs`/`IAnalyticsService.cs`,
`AnalyticsRepository.cs`/`IAnalyticsRepository.cs`, `AnalyticsDtos.cs`, `PosAnalyticsDtos.cs`, or
anything under `frontend/` — that's TASK-481/482/483+. (Unrelated frontend files show as modified
in `git status` — pre-existing working-tree state from elsewhere, not touched by this task.)

## Files

- `backend/ShelfGuard.Infrastructure/Authorization/AnalyticsAuthorization.cs` (new)
- `backend/ShelfGuard.Domain/Constants/TenantRoleCapabilities.cs`
- `backend/ShelfGuard.Tests/Authorization/AnalyticsAuthorizationTests.cs` (new)
- `.claude/docs/decisions.md`
