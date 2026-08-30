# Handoff TASK-643 → TASK-644 (qa-tester)

**From:** backend-developer (TASK-643, Parts A+B) · **Date:** 2026-08-30
**Read first:** `.claude/logs/tasks/643_2026-08-30_marketplace-provider-rls-impl_backend-developer.md`,
then TASK-641's threat model (its §7 test-plan additions are yours).

## State you're inheriting

Source changes are **in the working tree, uncommitted**. Build/tests green:
Release build 0 errors / 1 pre-existing warning; `dotnet test` **2023/2023** (baseline was **2014**,
not the 1953 quoted in the plan — use 2023 as your starting point, and note a concurrent session is
also adding tests in `ShelfGuard.Tests/Notifications/`).

## What changed that your tests must pin

- `MarketplaceRepository.SetProviderRoleAsync` is **gone**. All 12 cross-tenant reads now run inside
  `IProviderRlsOverride.ExecuteAsync` → `BEGIN; SET LOCAL app.role = 'provider'; …; COMMIT`.
- Two new composite methods carry the legitimate cross-tenant writes:
  `UpsertMetricsRatingAsync` (W1, `supplier_metrics` — **both** UPDATE and INSERT branches) and
  `SetReviewReplyAsync` (W2, `supplier_reviews`).
- `GetReviewByIdAsync` is **deleted** (R1) — don't write tests against it.
- `MarketplaceOrderService` now filters on the JWT-derived `clientTenantId` in three places
  (conflicts list, link-target resolution, collision set) and re-validates ownership again at the
  `_items.Update` write.

## Files to write (plan §Тести)

1. `backend/ShelfGuard.Tests/Infrastructure/MarketplaceOrderCatalogConflictsRlsIntegrationTests.cs`
2. `backend/ShelfGuard.Tests/Infrastructure/MarketplaceProviderBypassScopeRlsIntegrationTests.cs`

Harness: copy `SupplierAgreementMarkSignedRlsIntegrationTests.cs` — `[Collection("TENANT_ISOLATION_TESTS")]`,
`RlsAuditRoleFixture`, soft-skip, `EnableDynamicJson()`. **Wire the real
`ShelfGuard.Infrastructure.Services.ProviderRlsOverride`**, not the pass-through double — that file's
`BuildAgreementService` already shows the pattern (I updated it). A
`PassThroughProviderRlsOverride` exists in `ShelfGuard.Tests/Marketplace/` for EF-InMemory tests only;
using it in an RLS test would make the test vacuous.

## Highest-value assertions (don't drop these)

- On the **same open connection**, immediately after `GetSupplierItemsAsync`,
  `SELECT current_setting('app.role', true)` must be back to the session role (`'store_manager'`).
  This is the single assertion that proves the fix rather than its side effects.
- `CheckCatalogConflictsAsync` with an empty client catalog + a **third** tenant holding the same
  barcode → **empty** list. This is the one that must **fail before the fix**.
- Negative control (F2): `catalogAction:"link"` with the third tenant's `LinkedItemId` →
  `LinkedItemNotFoundError`, and that row's `SourceSupplierItemId` stays null.
  Per TASK-641 R6, also assert no row in the third tenant's `categories` / `product_segments` /
  `suppliers` was rewritten — `_items.Update` marks the whole `.Include`d graph Modified, so the
  old bug was a 4-table cross-tenant full-row rewrite, not 1.
- W1 must be exercised on the **INSERT** branch (first-ever review for a supplier), not only UPDATE —
  the plan's file-2 test as written could pass on UPDATE alone.
- Positive controls so the fix isn't over-corrected: `GET /api/marketplace/suppliers`,
  `/suppliers/{id}`, `/suppliers/{id}/items`, `/suppliers/search` must still return cross-tenant
  public data, and `GetSupplierItemImagesByIdsAsync`'s receipt-screen fallback photo must still resolve.

## Proving it failed before the fix

Plan step 5 is mandatory. **Do not use `git worktree`** — it breaks on this repo on Windows
(MAX_PATH on old migration filenames; see memory `git-worktree-windows-path-limit`). Use `git stash -u`
of the source changes only, keeping your test files, run the conflicts test, record the failure, then
`git stash pop`.

**Careful:** a concurrent session has unrelated uncommitted work in this same tree
(`NotificationsController`, `NotificationService`, `frontend/features/notifications/*`,
`CustomerMessageForm.tsx`, `mobile/*`). A blanket `git stash -u` will take theirs too. Stash by
pathspec — the TASK-643 source files are listed at the top of my task log — or coordinate first.

## Environment gotchas

- `docker compose up -d postgres` (port 5435) must be up; verify your run does **not** print
  "DB not available — skipped". Green-but-skipped is a failed verification.
- A dev `ShelfGuard.Api.exe` from another session may hold `ShelfGuard.Api/bin/Debug` and break
  `dotnet build` with `MSB3021`. That's a file lock, not a code error — build `-c Release` (what I
  used) or stop that server.

## Open item for TASK-645 (post-impl review), not for you

`IMarketplaceRepository.AddMetricsAsync` has no production caller left after W1. Kept because it is
staging-only (no bypass), so it isn't the escape-hatch class R1 targeted — but worth a decision.
