# TASK-618 — Customer detail view: tier/progress, open tickets, recent reviews (Features/Customers extension)

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §2 "Features/Customers (розширення)". Read task logs 613–617 for
exact method signatures per the brief; no re-derivation needed.

## What changed

Extended the existing `Features/Customers` (`CustomerDetailDto`, `CustomerService.GetByIdAsync`)
so the staff-facing customer detail view gets loyalty tier/progress, open-ticket count, and
recent reviews in one response — no N+1 from the frontend. Only read paths added; did not touch
`ConsumerProfile`, `Loyalty`, `PosService.cs`, `CustomerSupport`, or `Reviews` internals.

**`CustomerDetailDto`** (`CustomerDtos.cs`) gained: `CurrentTierName` (string?), `CompositeScore`
(decimal?), `TierProgressPercent` (decimal?), `OpenTicketCount` (int), `RecentReviews`
(`List<CustomerReviewSummaryDto>` — new DTO: `Rating`, `Comment`, `CreatedAt`, `ReplyText`).

**`CustomerService`** now takes `ILoyaltyRepository`, `IConsumerSupportTicketRepository`,
`IPurchaseReviewRepository` alongside the existing `ICustomerRepository` (all three already
registered in DI from TASK-615/616/617 — no DI changes needed). `GetByIdAsync` runs three
sequential reads after loading the customer (sequential, not `Task.WhenAll` — they share the
request's `AppDbContext`, not thread-safe for concurrent use):
`GetMembershipByCustomerIdAsync` → tier-progress math → `CountOpenByCustomerIdAsync` →
`GetRecentForCustomerAsync`. Acceptable per the brief: single-customer detail page, not a list.

## New repository methods (narrow additions, nothing existing fit)

- `ILoyaltyRepository.GetMembershipByCustomerIdAsync(customerId, tenantId, ct)` — mirrors
  `GetMembershipByIdAsync`'s `.Include(CurrentTier)` shape but keyed by `CustomerId` (existing
  methods were all keyed by membership Id or ConsumerAccountId, not CRM CustomerId).
- `IConsumerSupportTicketRepository.CountOpenByCustomerIdAsync(customerId, tenantId, ct)` —
  counts `Status` in Open/InProgress only.
- `IPurchaseReviewRepository.GetRecentForCustomerAsync(customerId, tenantId, take, ct)` —
  `PurchaseReview` carries no `CustomerId` of its own (TASK-617 resolved review ownership via the
  loyalty ledger, not this column); joins on the scalar FK through
  `PosTransaction.CustomerId` (`join t in _db.PosTransactions on r.PosTransactionId equals t.Id`)
  rather than navigation-property filtering, to keep the query provider-agnostic and testable
  against the InMemory provider.

All three manual test fakes/wrappers that implement `ILoyaltyRepository` directly (`PosServiceTests.cs`
`FakeLoyaltyRepo`, `FiscalizationRetryTests.cs` `RetryFakeLoyaltyRepo`,
`LoyaltyConcurrencySalesIntegrationTests.cs` `RendezvousLoyaltyRepository`) updated with a no-op/
delegating implementation of the new method so the extended interface still compiles.

## Tier-progress math (the one piece of real logic here)

- No linked `LoyaltyMembership` at all (walk-in) → `CurrentTierName`/`CompositeScore`/
  `TierProgressPercent` all null.
- Membership exists but `CurrentTierId` is null (not yet recomputed, or hasn't cleared even the
  lowest tier's threshold — see `LoyaltyMembership.CurrentTierId` doc) → `CompositeScore` is
  still reported (real, always-present value on the entity); `CurrentTierName`/
  `TierProgressPercent` stay null — "no tier assigned yet" per the brief. `GetTierLadderAsync` is
  skipped entirely in this case (no wasted query).
- Membership has a tier → `TierProgressPercent = CompositeScore / nextTier.MinCompositeScore *
  100`, clamped 0–100 (`nextTier` = lowest `SortOrder` above the current tier's own SortOrder).
  Null when already at the top tier (no next tier exists). Guarded against divide-by-zero
  (`MinCompositeScore <= 0` → 100).
- Interpreted "progress toward the next tier's MinCompositeScore" literally (score ÷ next
  threshold), not as a within-band progress bar (current-tier-threshold-relative) — the brief's
  wording matches the literal reading more directly and avoids an extra assumption about banding.

`OpenTicketCount`/`RecentReviews` are direct pass-throughs from the two new repository methods;
`recentReviews` is defensively coalesced to `[]` if a repository ever returns null (surfaced by
an NSubstitute default-return gap during test-writing, not a production code path — added the
guard anyway since it's free and harmless).

## Tests

7 new tests in `ShelfGuard.Tests/Customers/CustomerServiceTests.cs` (service-layer, NSubstitute):
no-membership nulls, membership-without-tier-yet (score shown, tier/progress null, ladder query
skipped), tier-assigned progress math, top-tier-null-progress, open-ticket-count pass-through,
zero-tickets/zero-reviews (empty list not null), recent-reviews DTO mapping + order preservation.

2 new repository-level InMemory-DB test files (pin the actual EF query/filtering, which mocking
the repository interface in the service tests above can't exercise):
- `ShelfGuard.Tests/CustomerSupport/ConsumerSupportTicketRepositoryCountOpenTests.cs` — confirms
  only Open/InProgress count (Resolved/Closed excluded), tenant/customer isolation, zero case.
- `ShelfGuard.Tests/Reviews/PurchaseReviewRepositoryGetRecentForCustomerTests.cs` — confirms the
  join-through-PosTransaction filtering, newest-first ordering, `take` limit, empty case.

## Build / test status

`dotnet build`: 0 errors, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs:534`).
`dotnet test` full suite: **1923/1923 passing** (13 new — 7 service-layer + 6 repository-layer —
zero regressions; baseline was 1910/1910 after TASK-617).

Note on build environment: a stray `ShelfGuard.Api.exe` process (unrelated, likely a concurrent
session's `dotnet run`) held a file lock on the Debug output during this task's verification —
worked around by building/testing in `-c Release` instead of killing the process, since it wasn't
mine to stop. Both build and full test run are clean under Release.

## Handoff

`.claude/logs/handoffs/618-to-frontend_backend-developer.md` — final `CustomerDetailDto` shape
for TASK-621 (frontend, customer card tabs).

## Not implemented here (separate follow-up tasks per plan §5)

Frontend consumption of these new fields (TASK-621, plan §4 "Картка клієнта"). `mobile/`
untouched (owned by a separate concurrent agent).
