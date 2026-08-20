# TASK-580 — Transfers: fail-closed destination-store check on PUT /api/transfers/{id}/confirm

**Status:** done · **Agent:** security-reviewer · **Updated:** 2026-08-20

## Gap

`TransferService.ConfirmAsync` had zero store-membership checks. Any user whose role passed the
controller's `CanReceiveStock` policy (`provider`, `enterprise_admin`, `network_manager`,
`store_manager`, `storekeeper`) could confirm ANY transfer in the tenant, including ones for
stores they have no `user_locations` assignment to — an IDOR-style authorization gap, not covered
by ADR-022's Stage 3 RLS (`stock_transfers`/`product_stock` writes here go through the app's own
service layer, not a store_scope-filtered read).

## Fix

Plugs into the existing ADR-022 `user_locations` mechanism (same repo, same DTO shape as
`LocationService.GetAllAsync`) rather than inventing a new one — but **fail-closed**, not
fail-open like that precedent (which is an intentionally transitional, cosmetic-list-only
choice per its own doc comment; a stock-mutating action doesn't get that exemption, and the
project's own coverage-gap report against prod confirmed zero active scoped-role users are
currently unassigned, so there's no legacy population this newly breaks).

- `TransferService.cs`: `ConfirmAsync` now takes `Guid tenantId, string? role`. New
  `StoreScopedConfirmRoles` set (`network_manager`, `store_manager`, `storekeeper` — the only
  three of the controller's five admitted roles that aren't an ADR-022 bypass rank), built from
  `Domain.Constants.AppRoles` per the Application→Infrastructure layering rule
  (`LocationService.StoreScopedRoles`'s documented rationale). Injects `IUserLocationRepository`
  (already DI-registered). Check runs `if (role is null || StoreScopedConfirmRoles.Contains(role))`
  — **deliberate deviation from the literal reference snippet in the brief**: a naive
  `StoreScopedConfirmRoles.Contains(role)` gate (no null branch) would let a null/missing role
  silently skip the check and succeed, which is the wrong default for a fail-closed check. Placed
  after the not-found/already-received/cancelled guards, before any stock mutation. Rejects with
  `"You do not have access to confirm transfers for this store."` when
  `GetLocationIdsForUserAsync(tenantId, confirmedBy)` doesn't contain `transfer.ToStoreId`.
- `ITransferService.cs`: interface signature updated to match.
- `TransfersController.cs`: `Confirm` action now also requires `tenantId` (was previously only
  checking `userId`, mirroring `Create`'s existing `Forbid()`-on-missing-context pattern), reads
  `User.FindFirstValue(ClaimTypes.Role)` (same idiom as most other controllers — JWT always
  carries this claim per `JwtService.cs`), passes both through. New error string mapped to
  `Forbid()` (403), inserted before the generic `BadRequest` fallback, alongside the existing
  `NotFound()` mapping.
- `CancelAsync` and `LocationService`'s fail-open behavior intentionally untouched (out of scope
  per brief). `CancelAsync` is already gated by `AtLeastStoreManager` (a stricter role floor) but
  has the same *kind* of missing-store-check gap — flagged as a follow-up, not fixed here.

## Tests

`TransferServiceTests.cs`: constructor now wires `IUserLocationRepository` with a default
"confirming user assigned to destination store" stub so the 3 pre-existing `ConfirmAsync` tests
keep passing unmodified in intent. Added:
- `ConfirmAsync_ScopedRoleAssignedToDestStore_Succeeds` (theory: network_manager/store_manager/
  storekeeper)
- `ConfirmAsync_ScopedRoleNotAssignedToDestStore_RejectsAndDoesNotMutateStock` (theory, same 3
  roles) — asserts the error string, transfer status unchanged, and via mock verification that
  `AddStockAsync`/`AddMovementAsync`/`Update`/`SaveChangesAsync` were never called
- `ConfirmAsync_ScopedRoleWithNoLocationAssignments_Rejects` (zero rows, not just wrong store)
- `ConfirmAsync_BypassRoleWithZeroLocationAssignments_Succeeds` (theory: provider/
  enterprise_admin) — also asserts `GetLocationIdsForUserAsync` was never even called
- `ConfirmAsync_NullRole_IsRejectedNotBypassed`

## Verification

- `dotnet build` — clean, 0 errors, 1 pre-existing unrelated warning (Marketplace tests).
- `dotnet test --filter "FullyQualifiedName~Transfers"` — 26/26 passed.
- Full suite (`dotnet test`, no filter) — 1748/1748 passed.
- No browser/HTTP verification performed — backend-only change, fully covered by the unit suite
  above; judged not worth the live-DB setup time for this task.

## Deviations from brief

1. Null-role handling: brief's literal reference snippet (`StoreScopedConfirmRoles.Contains(role)`
   with no null branch) would let a missing role claim bypass the check; implemented
   `role is null || StoreScopedConfirmRoles.Contains(role)` instead so null falls into the checked
   (and rejected, absent a matching grant) path — matches the brief's own stated intent ("null
   should be treated as NOT bypassing"), just not literally the sample code shown.
2. Test theories use `[InlineData(AppRoles.X)]` directly rather than `nameof`+switch — the
   `AppRoles` fields are `const string`, so they're valid compile-time attribute arguments as-is.

Log: `.claude/logs/tasks/580_2026-08-20_transfer-confirm-store-scope_security-reviewer.md`.
