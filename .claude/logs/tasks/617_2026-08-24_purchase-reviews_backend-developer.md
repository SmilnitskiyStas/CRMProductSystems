# TASK-617 — Consumer purchase review channel (Features/Reviews)

**Agent:** backend-developer · **Status:** done · **Date:** 2026-08-24

Plan: `C:\Users\stass\.claude\plans\goofy-bubbling-naur.md` §2 "Features/Reviews".
Handoff read: `.claude/logs/handoffs/613-to-backend_database-engineer.md` (TASK-613 schema).

## What changed

New `Features/Reviews` module, mirroring the existing `SupplierReview`/`MarketplaceService`
rating+comment+one-reply pattern, but keyed to a `PosTransaction` instead of a `Supplier`. The
`PurchaseReview` entity + RLS (canonical triad + `consumer_self_access`) already existed from
TASK-613 — this task is the service/repository/controller layer on top of it.

**New files:**
- `backend/ShelfGuard.Domain/Interfaces/IPurchaseReviewRepository.cs`
- `backend/ShelfGuard.Domain/Exceptions/DuplicateReviewException.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/PurchaseReviewRepository.cs`
- `backend/ShelfGuard.Application/Features/Reviews/IReviewService.cs`
- `backend/ShelfGuard.Application/Features/Reviews/ReviewService.cs`
- `backend/ShelfGuard.Application/Features/Reviews/Dtos/ReviewDtos.cs`
- `backend/ShelfGuard.Api/Controllers/ConsumerReviewsController.cs`
- `backend/ShelfGuard.Api/Controllers/ReviewsInboxController.cs`
- `backend/ShelfGuard.Tests/Reviews/ReviewServiceTests.cs`

**Edited:**
- `backend/ShelfGuard.Application/DependencyInjection.cs` — registered `IReviewService` after
  the TASK-616 registration (re-read fresh immediately before editing, per the brief).
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` — registered
  `IPurchaseReviewRepository` after the TASK-616 registration (same re-read-fresh discipline).

## Ownership-resolution approach (the core design problem)

`PosTransaction` has no direct `ConsumerAccountId` FK — only `CustomerId` (the tenant CRM
record). I resolved this via the ledger join rather than the CustomerId join:

```
LoyaltyLedgerEntry.PosTransactionId → MembershipId → LoyaltyMembership.ConsumerAccountId
```

Reason: `ILoyaltyRepository.GetLedgerEntriesForTransactionsAsync` already exists (added for
TASK-410, backing `PosService.GetSalesForShiftAsync`) and its own doc comment states this is
"the only persisted signal that loyalty activity happened on that sale" — a stronger, more
direct claim than the CustomerId path, which would require assuming CustomerId maps back to
exactly one membership (true in practice via `FindOrCreateCustomerAsync`'s phone-match, but not
a stated invariant anywhere). I called that existing method directly, no new repository method
needed for this half of the join.

`ReviewService.IsOwnPurchaseAsync`:
1. `_loyalty.GetLedgerEntriesForTransactionsAsync(tenantId, [posTransactionId])` — empty result
   means either a walk-in transaction (no loyalty link at all) or a transaction that RLS's own
   `consumer_self_access` policy on `loyalty_ledger_entries` (TASK-404) already hid because it
   belongs to a different consumer's membership. Both cases are treated identically: reject.
2. If a ledger entry exists, load its membership via `GetMembershipByIdAsync` and explicitly
   check `membership.ConsumerAccountId == consumerAccountId` — belt-and-suspenders on top of the
   RLS filtering in step 1, not the only guard.

Both rejection paths return the same generic message + 403 — never discloses which of "not your
purchase" / "no loyalty link" applies, matching the uniform-rejection convention
`ConsumerSupportService.GetTicketAsync` already uses for its 404s.

**No `ITenantSessionOverride` anywhere in this feature** — a departure from `LoyaltyService`/
`ConsumerSupportService`, which need it for tables with only the canonical `tenant_isolation`
RLS policy. Every table `ReviewService` touches under a consumer session already has an
identity-based policy that admits the caller's own rows directly: `purchase_reviews` (direct
`ConsumerAccountId` column, TASK-613), `loyalty_memberships`/`loyalty_ledger_entries`
(TASK-404), and `tenants` (no tenant-scoping RLS on its own table at all — same reason
`ConsumerSupportService`/`LoyaltyService` read it directly, unguarded).

## Duplicate guard (two layers)

1. **Pre-check:** `IPurchaseReviewRepository.GetByTransactionAsync(posTransactionId)` before
   insert — covers the non-racing case with a clean 409, no DB round-trip wasted on a doomed
   insert.
2. **DB backstop:** `uq_purchase_reviews_pos_transaction` (the unique index from TASK-613) is
   the real enforcement. `PurchaseReviewRepository.SaveChangesAsync` catches the Npgsql
   `PostgresException` (SqlState 23505, matching that constraint name) and translates it to a
   new `DuplicateReviewException` (Domain-level, mirrors `ConcurrencyConflictException`'s
   EF-Core-stays-in-Infrastructure translation pattern) — `ReviewService.CreateReviewAsync`
   catches that and returns a clean 409 instead of letting it fall through to
   `GlobalExceptionHandler`'s generic 500.

## Reply guard — deliberate divergence from SupplierReview

Brief explicitly asked for "one reply only, reject if ReplyText already set." I checked
`SupplierCabinetService.ReplyToReviewAsync` (the actual reply endpoint for `SupplierReview`) —
it does **not** guard against a second reply; it silently overwrites `ReplyText`/`RepliedAt`.
I implemented the guard anyway (409 on a second attempt) per the explicit brief instruction and
`PurchaseReview`'s own class doc, which already states "one reply per review" as design intent
from TASK-613. Noting the divergence here since the brief asked me to "check and be consistent"
— consistency lost to the stronger, more specific instruction and the entity's own documented
intent.

## Endpoints

- `POST /api/consumer/reviews` — `[Authorize]`, `consumer_account_id` claim. Body: `TenantId`,
  `PosTransactionId`, `Rating` (1-5), `Comment?`. 201 / 400 / 403 / 404 / 409.
- `GET /api/consumer/reviews?tenantId=&page=&pageSize=` — this consumer's own reviews at one
  tenant, paged.
- `GET /api/reviews?rating=&page=&pageSize=` — staff inbox, `AppPolicies.AtLeastStoreManager`
  (same tier as `CustomerSupportInboxController`, not admin-only — deliberately not
  `[RequireModule]`-gated, matching that controller's own reasoning).
- `PUT /api/reviews/{id}/reply` — staff reply, one per review.

## Tests

14 new unit tests, `ShelfGuard.Tests/Reviews/ReviewServiceTests.cs` (NSubstitute, mirrors
`ConsumerSupportServiceTests` conventions):
- `CreateReviewAsync`: legitimately-owned transaction succeeds; different-consumer's transaction
  rejected (403); no-loyalty-link transaction rejected (403); existing review on the transaction
  rejected without an insert attempt (409); DB unique-violation race rejected gracefully (409,
  via a mocked `DuplicateReviewException` thrown from `SaveChangesAsync`); rating 0/6/-1 all
  rejected (400) without touching the loyalty repository at all; unknown consumer/tenant → 404.
- `ReplyAsync`: first reply succeeds; second attempt rejected (409); wrong-tenant review → 404;
  blank reply → 400 without a repository lookup.

## Build / test status

- `dotnet build`: **0 errors**, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs:534`, nullable dereference, not touched by this task).
- `dotnet test` (full suite, `--no-build`): **1910/1910 passing** — baseline was 1896/1896 after
  TASK-616; this task adds exactly 14, zero regressions.

## Not in scope (separate follow-up tasks per plan §5)

`Features/Customers` extension (recent-reviews on the customer card), worker tier-recompute job,
frontend (`/customer-support` reviews tab, mobile "Leave a review" screen off
`history.tsx`/`[id].tsx`). `mobile/` untouched — owned by a separate concurrent agent per
existing session convention.
