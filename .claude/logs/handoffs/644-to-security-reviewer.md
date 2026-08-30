# Handoff TASK-644 → TASK-645 (security-reviewer — independent post-impl diff review)

**From:** qa-tester (TASK-644) · **Date:** 2026-08-30
**Read first:** `.claude/logs/tasks/644_2026-08-30_marketplace-provider-rls-qa_qa-tester.md`, then
TASK-643's impl log and TASK-641's threat model (§7 R1–R7).

## State you're inheriting

TASK-643 source + TASK-644 tests are all **in the working tree, uncommitted**. Nothing committed.
A concurrent session is also working in this tree (notifications / CustomerMessage campaigns +
a new migration `20260830143000_AddCustomerMessageCampaignSnapshots.cs`) — **not** part of
TASK-641..646, leave it alone; `git diff` before you stage anything.

## What TASK-644 added (test-only)

Two live-Postgres RLS integration files in `backend/ShelfGuard.Tests/Infrastructure/`:
`MarketplaceOrderCatalogConflictsRlsIntegrationTests.cs` (4 facts) and
`MarketplaceProviderBypassScopeRlsIntegrationTests.cs` (6 facts). No production source touched.

- The headline test was **proven to fail on pre-fix sources** (targeted-pathspec `git stash`, not
  worktree, not blanket `-u`) — verbatim failure output is in the task log: a client with an empty
  catalog got the third tenant's `Item` (id/name/imageUrl/barcodes) back as a "conflict", and
  `app.role` was left `'provider'` on the connection.
- Highest-value assertion wired: on the same still-open connection after each bypass call,
  `current_setting('app.role')` is back to `'store_manager'`.
- R6 covered: F2 write-vector negative control asserts the third tenant's `items` +
  `categories` + `suppliers` rows are byte-unchanged after a rejected `link`; W1 is exercised on
  **both** the `supplier_metrics` INSERT branch (first-ever review) and the UPDATE branch.
- Positive controls: `db.Items.CountAsync()` sees own-tenant only; public marketplace reads still
  cross-tenant.

## Verification status

- `dotnet test --filter "…MarketplaceOrderCatalogConflictsRls|…MarketplaceProviderBypassScope"` →
  **10/10 passed, 0 skipped** (both files executed against real Postgres :5435, no soft-skip).
- Full suite `dotnet test ShelfGuard.sln -c Release` → **2034/2034 passed, 0 failed, 0 skipped**
  (TASK-643 baseline 2023 + this task's +10 + concurrent session's +1; zero regressions).
- Debug build is blocked by a concurrent `bin/Debug` lock — Release only (identical sources).

## For your review (TASK-645 scope, unchanged from the plan)

No bug was found in the TASK-643 fix while authoring these tests. Your independent diff review still
owns: no `GetDbConnection()` in `MarketplaceRepository`; every bypass query inside an
`ExecuteAsync` block; no block wraps an outward call or a stray `SaveChangesAsync`; the two
composite methods can't be repurposed into an escape hatch; `IProviderRlsOverride` resolves nowhere
but `MarketplaceRepository` (`ProviderRlsOverrideContainmentTests` asserts this); the Part B filters
are on JWT-derived `clientTenantId`. Open item from TASK-643: `IMarketplaceRepository.AddMetricsAsync`
has no production caller left after W1 — confirm or remove.
