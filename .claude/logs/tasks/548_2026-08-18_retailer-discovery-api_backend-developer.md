# TASK-548 — Retailer discovery API

**Status:** done
**Agent:** backend-developer
**Date:** 2026-08-18

## What was built

New consumer-session-authenticated (`consumer_account_id` claim, same posture as
`ConsumerLoyaltyController`) controller under the versioned surface:

- `GET /api/v1/retailers` — lists retailers available to the calling consumer. Reuses
  `ILoyaltyService.GetAvailableNetworksAsync` as-is (same loyalty-module/active/settings-enabled
  eligibility rule as today's `GET /api/consumer/loyalty/networks`).
- `GET /api/v1/retailers/{slug}` — single retailer lookup by slug. 404 for an unknown slug, an
  inactive tenant, a tenant without the `loyalty` module, or a tenant whose loyalty settings row
  has `IsEnabled = false` — all indistinguishable "not found" to the caller, matching how the list
  endpoint simply omits the same tenants (decision 1's accepted consequence).
- `POST /api/v1/retailers/{slug}/join` — resolves slug → tenant id, then delegates to the
  existing `ILoyaltyService.JoinAsync` (unchanged join semantics/status codes: 403 module not
  active, idempotent on repeat).
- `DELETE /api/v1/retailers/{slug}/membership` — new leave capability (see below). 204 on
  success/idempotent-repeat, 404 when the consumer has no membership at that retailer (including
  an unknown slug).

New file: `backend/ShelfGuard.Api/Controllers/RetailersController.cs`.

`ConsumerLoyaltyController.cs` itself was **not modified** — only reused via the service layer,
per the brief's preference.

## Leave capability — design decision

`LoyaltyMembership.Status` previously only had `active`/`blocked`
(`backend/ShelfGuard.Domain/Entities/LoyaltyMembership.cs`). Added a third value, `left`
(`LoyaltyMembershipStatus.Left`), and two service methods:

- `ILoyaltyService.LeaveAsync(consumerAccountId, tenantId, ct)` — soft-deactivates the membership
  (`Status = "left"`) rather than deleting the row. `Balance`/`JoinedAt`/`LedgerEntries`/
  `TotpSecret` are left untouched — same never-hard-delete-financial-history precedent as
  `Customer.TotalSpent`/`Tenant.Deactivate`. Idempotent: leaving an already-left membership
  returns success again without a redundant `UpdateMembership`/`SaveChangesAsync`. Not gated on
  the tenant's current module/active state — a consumer must always be able to leave, even if the
  retailer later disabled loyalty.
- `LeaveBySlugAsync(consumerAccountId, slug, ct)` — thin slug-resolution wrapper, 404 on an
  unknown slug, otherwise delegates to `LeaveAsync`.

**The other half of the gap, found while implementing:** before this task, `JoinAsync`'s
idempotent branch (an existing `LoyaltyMembership` row found for the tenant/consumer pair) just
returned that row unchanged. Once `left` became a real status, that would have made leaving a
one-way door — rejoining via either the old or new join endpoint would hand back a membership
still stuck at `Status = "left"`, silently blocking POS redemption (`ResolveCodeAsync` already
rejects any non-`active` status). Fixed by having `JoinAsync` reactivate a `left` membership back
to `active` (single `UpdateMembership` + `SaveChangesAsync`) when it's the one found, while
leaving an already-`active` idempotent rejoin as a pure no-op (pinned by a dedicated test — no
extra write). `Balance`/`JoinedAt`/history are preserved across a leave→rejoin cycle, never reset.
This only touches the `loyalty_memberships` table, which already carries the `consumer_self_access`
RLS policy for this session shape (covers `UPDATE`, not just `SELECT` — policy has no `FOR`
clause, so it applies to all commands) — no new `ITenantSessionOverride` wrapping needed, same as
the pre-existing idempotency check itself.

`GetMembershipsForConsumerAsync`/`GetHistoryAsync` (used by the kept-as-alias
`GET /api/consumer/loyalty/memberships`/`.../history`) were deliberately **not** changed to filter
out `left` memberships — `Status` was already a plain field consumers/staff could see (`blocked`
existed with no special filtering either), and adding filtering there would be a behavior change
to an endpoint this task was told to leave untouched. A left membership now simply shows up with
`"status": "left"`, same as `"blocked"` does today. Noted here in case a future task wants to hide
left networks from the wallet list — that's a product/UX call, not made in this task.

## Slug lookup addition

`ITenantRepository.GetBySlugAsync(slug, ct)` (+ `TenantRepository` implementation) — single-tenant
lookup by slug, case-insensitive (`ToLowerInvariant()`, same normalization `Tenant.Create`/
`SlugExistsAsync` already apply). Returns the tenant regardless of `IsActive`/module state;
callers (here, `LoyaltyService`) decide what "not found" means. The `tenants` table carries no RLS
policies at all (confirmed by grepping migrations), so this needed no `ITenantSessionOverride`,
consistent with how `GetByIdAsync` is already called directly from `JoinAsync`'s ambient consumer
session today.

## DTO change

`LoyaltyNetworkSummaryDto` gained a `Slug` field (sourced from `Tenant.Slug`), inserted right after
`TenantName`. Purely additive — the one construction site
(`LoyaltyService.GetAvailableNetworksAsync`) was updated; no test or caller constructed this
record positionally elsewhere, so nothing else needed touching. This means the pre-existing
`GET /api/consumer/loyalty/networks` response now also carries `slug` per entry — an additive
JSON field, not a behavior change to any existing field.

## Old endpoints — kept as a permanent alias

`GET /api/consumer/loyalty/networks` and `POST /api/consumer/loyalty/{tenantId}/join`
(`ConsumerLoyaltyController.cs`) were not touched at all beyond the additive `Slug` field flowing
through the shared DTO. Per the brief's guidance, this is recorded as a **permanent, un-deprecated
alias** — no deprecation timeline was invented. If/when the mobile client fully migrates to
`/api/v1/retailers`, a future task can decide whether/when to deprecate the old routes; that
decision needs product input this task wasn't asked to make.

## Tests

All new coverage is mock-based (`NSubstitute`), matching this repo's existing convention for
`LoyaltyService` (no `WebApplicationFactory` HTTP harness exists here — confirmed by TASK-547's
prior note, still true).

- `backend/ShelfGuard.Tests/Auth/LoyaltyServiceTests.cs` — 15 new tests:
  `GetNetworkBySlugAsync` (unknown slug, inactive tenant, no module, settings disabled, eligible
  tenant incl. `Slug`/`Stores`), `JoinBySlugAsync` (unknown slug, delegates correctly, module gate
  403), `LeaveAsync`/`LeaveBySlugAsync` (no membership 404, sets `left` + persists, idempotent
  no-op, slug delegation), `JoinAsync` rejoin-after-leave reactivation (+ a no-op pin for an
  already-active rejoin). Also added a `Slug` assertion to the pre-existing
  `GetAvailableNetworksAsync_returns_only_active_enabled_loyalty_networks` test.
- `backend/ShelfGuard.Tests/Provider/TenantRepositoryGetBySlugTests.cs` — new file, 4 tests
  (`InMemoryDatabase`, same pattern as `TenantRepositoryPlatformTenantTests`): match, case
  insensitivity, unknown slug, inactive tenant still returned (caller decides eligibility).

## Verification actually performed this run

- `dotnet build ShelfGuard.sln` — succeeded, 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test ShelfGuard.sln --no-build` — **1673 passed, 0 failed, 0 skipped** (up from 1654 at
  TASK-547; includes the live-Postgres RLS/loyalty integration suites — DB was reachable).
- `git status`/`git diff --stat` confirmed the change is scoped to exactly: the new
  `RetailersController.cs`, `ILoyaltyService`/`LoyaltyService`/`LoyaltyDtos`/`LoyaltyMembership`,
  `ITenantRepository`/`TenantRepository`, and the two test files above.
  `ConsumerLoyaltyController.cs` was not touched.
- No browser/manual/live HTTP verification was performed — build + test output only.

## Independent re-verification (backend-developer, same day, resumed session)

This implementation was found already complete and uncommitted after an interrupted run; this
entry documents an independent confirmation pass rather than a re-implementation.

- Read the full diff/new-file content of every changed file listed above and confirmed by direct
  reading (not inference) that: `ConsumerLoyaltyController.cs` has zero uncommitted changes
  (`git diff --stat` empty) and its `GetNetworks`/`Join` actions call the exact same
  `ILoyaltyService.GetAvailableNetworksAsync`/`JoinAsync` methods `RetailersController` calls, so
  the old endpoints are a true behavioral alias, not just untouched source; the `loyalty`-module
  gate is enforced inside `LoyaltyService` (`GetNetworkBySlugAsync`, `JoinBySlugAsync` →
  `JoinAsync`) exactly as documented; `LeaveAsync` sets `Status = "left"` without touching
  `Balance`/`JoinedAt`/`LedgerEntries`/`TotpSecret` (confirmed against the entity and the pinned
  `LeaveAsync_active_membership_sets_status_left_and_persists_without_touching_balance` test); the
  new `LoyaltyServiceTests.cs` cases and `TenantRepositoryGetBySlugTests.cs` genuinely exercise
  list/get/join/leave plus the loyalty-gate exclusion case (unknown slug, inactive tenant, missing
  module, `IsEnabled = false`).
- Fixed one stale doc comment found during review:
  `backend/ShelfGuard.Domain/Entities/LoyaltyMembership.cs`'s `Status` field summary still read
  `active | blocked`, not updated when `left` was added a few lines below in the same file. Changed
  to `active | blocked | left`. No behavior change.
- Re-ran verification independently after that edit: `dotnet build ShelfGuard.sln` — 0
  errors/0 warnings (the earlier "1 pre-existing unrelated warning in MarketplaceServiceTests.cs"
  noted above did not reproduce in this clean build — likely incremental-build noise, not a
  regression; not investigated further as out of this task's scope). `dotnet test ShelfGuard.sln
  --no-build` — **1673 passed, 0 failed, 0 skipped**, matching the count above.
- No browser/manual/live HTTP verification was performed here either — build + test output only.
