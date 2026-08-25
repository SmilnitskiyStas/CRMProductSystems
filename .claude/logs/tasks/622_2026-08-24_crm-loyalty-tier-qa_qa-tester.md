# TASK-622 — End-to-end QA: loyalty tier ladder, consumer profile, support tickets, purchase reviews

**Status:** done · **Agent:** qa-tester · **Date:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §9. Read all of TASK-613..621b task logs + handoffs before testing.
Scope: backend (.NET) + web (Next.js) only, per plan. `mobile/` not touched.

## Result summary

No bugs found. One pre-existing, already-documented limitation confirmed benign. All 7 priority
areas from the brief: **confirmed-working**.

## 1. Checkout regression safety — confirmed-working

`dotnet test` (full suite, backend/): **1925/1925 passing**, matches TASK-621b's baseline exactly
— zero drift from other concurrent repo work. `PosServiceTests.cs` has 3 real tier tests (not
no-throw checks): `CreateSale_membership_without_tier_behaves_exactly_as_before` (regression pin),
`CreateSale_tier_accrual_multiplier_scales_bonus` (asserts 10→15 accrual on a 1.5× tier),
`CreateSale_tier_discount_reduces_item_total_and_accrual_base` (asserts 100→90 item total, 9m
accrual off the net base). `LoyaltyServiceTests.cs` has 16 tests covering ladder CRUD (incl.
duplicate-sortOrder rejection, sortOrder-matching preserving Id, tier removal), progress (3
null-states), and history.

## 2. PosService tier arithmetic — confirmed-working

Read `PosService.cs` end to end around the integration points (lines ~253-330, ~448-452).
Tier discount is computed off `priceRetail` once per item, additively combined with the
critical-batch auto-discount, capped at the item price — folds into `PriceFinal`/`DiscountAmount`
once, so `tx.TotalAmount` (sum of item totals) reflects it exactly once. Accrual multiplier is
applied independently, to the accrual formula only, on the post-redemption `tx.TotalAmount`. No
double-counting, no compounding-order bug. `GetMembershipByIdAsync`/`GetMembershipByCustomerIdAsync`
both `.Include(m => m.CurrentTier)` — verified in `LoyaltyRepository.cs`.

## 3. RLS on the 6 new tables — confirmed-working

Structural check: `pg_class.relrowsecurity/relforcerowsecurity` — `f/f` on
`consumer_account_profile_changes` (no RLS, by design), `t/t` on the other 5. Matches TASK-613's
claims exactly.

Functional check (direct SQL against dev DB, seeded fixture rows inside a rolled-back
transaction — see `consumer_support_tickets`/`purchase_reviews`):
- Consumer A session (`app.consumer_account_id` set, `app.role='consumer'`) sees only its own
  ticket/review; consumer B sees only its own; a random/unmatched consumer id sees 0 rows.
- Staff session of the owning tenant (`app.role='store_manager'`, `app.tenant_id` set) sees ALL
  tickets in that tenant (including a pre-existing TASK-621 fixture) regardless of which consumer
  owns them.
- Staff session of a *different* tenant sees 0 rows.
- `worker_bypass` (`app.role='worker'`) sees everything regardless of tenant/consumer vars.
- Querying with **no** session vars set (the default state for a fresh non-superuser connection)
  returns 0 rows on every policy — fail-closed by default, as intended (also explains an initial
  false alarm: querying `consumer_support_tickets`/`purchase_reviews` directly as
  `shelfguard_app_dev` without `SET`ting any session vars returned empty, which looked like the
  dev DB had been wiped since TASK-621's own verification pass — it hadn't; the data was there,
  RLS was just correctly blocking the unscoped query).

## 4. Review authorization edge cases — confirmed-working

Read `ReviewService.CreateReviewAsync`/`IsOwnPurchaseAsync` directly. Both rejection paths
(transaction belongs to a different consumer; transaction never linked to any loyalty membership
at all / walk-in) fall through the same `IsOwnPurchaseAsync` → `false` → 403 branch, generic
message, no path to 500. Already covered by 2 of the 14 `ReviewServiceTests.cs` cases
(`different-consumer's transaction rejected`, `no-loyalty-link transaction rejected`).

## 5. Frontend smoke test — confirmed-working

Started `backend-dev`/`frontend-dev` via `.claude/launch.json` against the existing dev Postgres.
Session was already authenticated from a prior verification pass.

- `/customer-support`: both tabs (Tickets, Reviews) render the leftover TASK-621 fixture data
  correctly — ticket shows "Resolved" status, review shows 5/5 with its staff reply.
- `/customers` → opened "TASK-410 Live Check Customer" drawer, clicked through all 5 tabs: Info
  (unchanged), Loyalty ("Customer hasn't joined the loyalty program" — correct not-enrolled
  state), Tickets ("0 Open tickets" — correctly excludes the Resolved fixture ticket from the open
  count), Reviews (renders the 5/5 review + reply), Profile history ("No change history yet" —
  correct empty state, fetched via `GET /api/customers/{id}/profile-history` confirmed in the
  network panel).
- "Open in inbox" from the Tickets tab correctly navigated to
  `/customer-support?customerId=<id>` with the ticket list pre-filtered to just that customer
  (confirmed via page text: "Showing tickets for this customer only").
- No console errors from any of the new code (only the pre-existing benign 401→refresh pairs on
  `/api/auth/me`, present repo-wide).
- Note: the `computer` tool's coordinate/ref clicks were unreliable in this session's browser pane
  too (same issue TASK-621 logged) — used `javascript_tool` dispatching real `click()` events on
  resolved DOM nodes to drive already-built UI, same workaround, no app code touched by it.

## 6. `?customerId=` deep-link page-size limitation — flagged-for-follow-up, not a blocker

Confirmed `TicketList.tsx`'s `CUSTOMER_FILTER_PAGE_SIZE = 200` genuinely matches the backend
ceiling (`Pagination.cs`: `ClampedPageSize => Math.Clamp(PageSize, 1, 200)`), so the frontend
can't ask for more even if it wanted to. As TASK-621 already noted: a customer whose tickets are
older than the 200 newest tenant-wide tickets (across *all* customers, not just this one) would
have some of their own older tickets silently missing from the filtered view, since
`GetInboxAsync` (TASK-616) has no server-side customer filter and pagination controls are hidden
in this mode. Unlikely in practice (would need a tenant with 200+ tickets ahead of a specific
customer's oldest one in newest-first order) but real. Worth a follow-up ticket to add a
`customerId` param to `IConsumerSupportService.GetInboxAsync` if/when a tenant's ticket volume
grows — not urgent now.

## 7. Worker tier-recompute job — confirmed-working (live SQL dry-run, not just code read)

Read `worker/src/jobs/loyalty-tier-recompute.job.ts` in full — quintile scoring convention
(`recencyScore = 6 - NTILE(5) OVER (... ASC)`, `frequency`/`monetaryScore = NTILE(5) ASC`),
equal-weight `(R+F+M)/3` composite, DESC-sortOrder tier matching, "only write if changed" epsilon
guard, `Balance` never touched — all as documented and as TASK-619 claimed.

Went further than a code read: ran the job's *exact* SQL (scoring query + write path) against the
dev Postgres inside two rolled-back transactions with seeded fixture data (no residue left):
- **Scoring query**, 5 synthetic memberships with perfectly-correlated R/F/M ranking (member 1
  worst on all 3 axes, member 5 best) → query returned recency/frequency/monetary scores of
  1,2,3,4,5 respectively as expected, confirming the NTILE quintile logic is correct.
- **Write path**, 1 synthetic membership (solo in its tenant, so `NTILE(5)` puts it in bucket 1 on
  every axis — R=5,F=1,M=1 → composite 2.3333) with a 2-rung ladder (Bronze min 0, Gold min 4) →
  ran the job's exact `UPDATE loyalty_memberships` + `INSERT loyalty_tier_change_history`
  statements → confirmed `CurrentTierId` set to Bronze, `CompositeScore` = 2.3333,
  `TierScoreUpdatedAt` set, and the history row recorded `FromTierId=NULL → ToTierId=Bronze` with
  correct `FromScore`/`ToScore`. Matches the job's intended behavior exactly.

Did not run the actual worker process (BullMQ/Redis) end-to-end — the direct-SQL dry-run covers
the part that actually matters (correctness of the query/write logic), consistent with
TASK-619's own verification approach.

## Not covered / out of scope

Did not touch `mobile/` (explicitly out of scope, owned by a separate concurrent agent).
Marketing-analytics tier segmentation — explicitly deferred as an optional later wave in the plan,
not part of this feature's QA scope.
