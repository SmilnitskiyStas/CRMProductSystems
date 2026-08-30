# TASK-643 — Marketplace provider-RLS leak: implementation (Parts A + B)

**Agent:** backend-developer · **Model:** opus · **Date:** 2026-08-30 · **Status:** done (uncommitted)
**Plan:** `snappy-dreaming-hanrahan.md` (Частини A+B; Частина C closed by TASK-642)
**Threat model:** `.claude/logs/tasks/641_2026-08-30_marketplace-provider-rls-pre-review_security-reviewer.md`
(required changes R1-R7; R3/R6/R7 are doc-only → TASK-646)

## What changed

**New primitive (Part A)**
- `backend/ShelfGuard.Application/Services/IProviderRlsOverride.cs` — `Task<T> ExecuteAsync<T>(Func<Task<T>>, CancellationToken)`,
  signature identical to `IAnalyticsRlsOverride`.
- `backend/ShelfGuard.Infrastructure/Services/ProviderRlsOverride.cs` — one-for-one shape of
  `AnalyticsRlsOverride.cs:17-33`: `BeginTransactionAsync` → `ExecuteSqlRawAsync("SET LOCAL app.role = 'provider'")`
  → `action()` → `CommitAsync`. Fixed literal ⇒ no `#pragma warning disable EF1002`. Does **not**
  join an ambient transaction (lets EF throw if anyone ever nests).
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` — `AddScoped<IProviderRlsOverride, ProviderRlsOverride>()`
  immediately after the `IAnalyticsRlsOverride` registration.

**`MarketplaceRepository.cs`** (`backend/ShelfGuard.Infrastructure/Data/Repositories/`)
- **`SetProviderRoleAsync` deleted entirely**, including `GetDbConnection()` + `conn.OpenAsync()` — the root cause.
- `IProviderRlsOverride` injected alongside `AppDbContext` (mirrors `MarketingAnalyticsRepository.cs:37-45`).
- Class-level XML doc rewritten (it documented the buggy session-level mechanism as the design).
- **12 bypass call sites** each wrap their existing body in ONE `ExecuteAsync` block:
  `GetPublicSuppliersAsync`, `CountPublicSuppliersAsync`, `GetSupplierByIdAsync`, `GetSupplierItemsAsync`,
  `GetSupplierItemImagesByIdsAsync` (the `Count == 0` early return stays **outside**),
  `SearchSuppliersAsync` (**both** dependent queries in **one** block), `GetSupplierByRawIdAsync`,
  `GetSupplierTenantIdAsync`, `GetReviewRatingsAsync`, `GetReviewsBySupplierAsync`,
  `CountReviewsBySupplierAsync`, `GetMetricsBySupplierIdAsync`.
- **Not wrapped** (staging-only, emit no SQL): `AddReviewAsync`, `AddSupplierAsync`, `AddSupplierProfileAsync`,
  `AddSupplierItemAsync`, `AddMetricsAsync`, `ReplaceItemBarcodes`, `ReplaceItemImages`,
  `RemoveSupplierItem`, `UpdateProfile`.
- `AsNoTracking()` added to `GetSupplierByRawIdAsync` and `GetMetricsBySupplierIdAsync` (F9).
- **W1** `UpsertMetricsRatingAsync(supplierId, supplierTenantId, rating, ct)` — new; load-or-create +
  `SaveChangesAsync` in ONE block (covers both the UPDATE and the cross-tenant INSERT branch).
- **W2** `SetReviewReplyAsync(supplierId, reviewId, replyText, repliedAt, ct)` — new; `.Include(r => r.Tenant)`
  filtered `Id == reviewId && SupplierId == supplierId`, returns `null` when absent (preserves
  "never reveal existence"), sets fields, saves, returns entity.
- Both composites carry the F10 caller contract in XML doc (they flush ANY pending tracked change
  under the provider role ⇒ callers must flush their own writes first).

**Interface** `backend/ShelfGuard.Domain/Interfaces/IMarketplaceRepository.cs` — `GetReviewByIdAsync`
removed, `UpsertMetricsRatingAsync` + `SetReviewReplyAsync` added with full contracts.

**Call sites**
- `MarketplaceService.RecalculateRatingAsync` (`:134`) — ratings read + `UpsertMetricsRatingAsync(...)`;
  its own `GetMetricsBySupplierIdAsync`/`AddMetricsAsync`/`SaveChangesAsync` dropped.
- `SupplierCabinetService.ReplyToReviewAsync` (`:206`) — `SetReviewReplyAsync(...)`, `null → ReviewNotFoundError`
  mapping kept, own `SaveChangesAsync` dropped.
- `IProviderRlsOverride` deliberately **not** injected into either service (ADR-028 §3 — repository-layer
  containment only).

**Part B — `MarketplaceOrderService.cs` defence in depth** (all on JWT-derived `clientTenantId`)
1. `CheckCatalogConflictsAsync` — `matches.FirstOrDefault(m => m.TenantId == clientTenantId)`.
2. `PlanCatalogOutcomeAsync` — now takes `Guid clientTenantId` (first param, matching
   `ExecuteCatalogPlanAsync`); `linkedItem is null || linkedItem.TenantId != clientTenantId` →
   `LinkedItemNotFoundError`; collision set filtered `.Where(i => i.TenantId == clientTenantId)`;
   doc comment rewritten (it asserted the disproved "ambient RLS resolves a foreign-tenant id to null").
3. `ExecuteCatalogPlanAsync` — re-validates `plan.LinkedItem!.TenantId != clientTenantId` before
   `_items.Update` (pass-1/pass-2 are loop-separated).

## How each required change was addressed

- **R1** — `GetReviewByIdAsync` deleted from both the interface and the implementation (not converted
  to `AsNoTracking()`); zero production callers remained once W2 moved. Cabinet tests re-pointed at
  `SetReviewReplyAsync`.
- **R2** — `IProviderRlsOverride`'s XML doc states: MarketplaceRepository-only; the measured
  **107-table** `provider_bypass` blast radius with the explicit "full cross-tenant read AND write,
  NOT marketplace-tables-only" framing; the one-operation/no-outward-call invariant; the ban on
  nesting inside `ITenantSessionOverride`/`IAnalyticsRlsOverride`; and the line recording that the
  old leak was bounded to one HTTP request only by Npgsql's default `DISCARD ALL` pool reset.
- **R4** — new `backend/ShelfGuard.Tests/Marketplace/ProviderRlsOverrideContainmentTests.cs`: two
  reflection tests over the Application + Infrastructure assemblies asserting `MarketplaceRepository`
  is the only type taking `IProviderRlsOverride` as a constructor parameter, and the only type
  holding it in a field (closes the property-injection/service-locator variant too). The ban on
  nesting is also stated in the XML doc per R4's first half.
- **R5** — the second copy of the disproved comment at `MarketplaceOrderReceiptService.cs:153-154`
  rewritten: it now says the invariant is false as a general rule, that it holds on this request
  only by call ordering (verified TASK-641 §6 F6), and names the exact check to add if a
  supplier-side lookup is ever hoisted above that line.
- **R3 / R6 / R7** — doc-only, left for TASK-646 as briefed. Facts they need are captured above and
  in the new XML docs.

## Tests

`backend/ShelfGuard.Tests/Marketplace/MarketplaceOrderServiceTests.cs` (+5 facts, 1 rewritten):
- `CheckCatalogConflicts_IgnoresBarcodeMatchOwnedByAnotherTenant` — the reported symptom.
- `CheckCatalogConflicts_ForeignAndOwnMatch_ReportsOnlyTheOwnTenantItem` — over-correction guard
  (foreign row sorted first must not mask a genuine own-tenant collision).
- `CreateOrder_LinkAction_LinkedItemNotOwnedByTenant_ReturnsError` — **rewritten**: the stub now
  returns a foreign-tenant `Item` with a *matching* barcode (what the real repo did under the leak)
  instead of `null`; asserts `LinkedItemNotFoundError`, no `Update`, no `SaveChangesAsync`, and
  `SourceSupplierItemId` untouched. Its old comment encoded the disproved assumption.
- `CreateOrder_LinkAction_MissingLinkedItem_StillReturnsSameNotFoundError` — keeps the
  indistinguishable-error property the rewrite could otherwise have lost.
- `CreateOrder_ForeignTenantBarcodeCollision_ProceedsAndAutoCreatesItem` — the functional half
  (bogus `BarcodeCollisionError` blocking legitimate orders).
- `CreateOrder_ForeignLinkOnSecondLine_NeverUpdatesAnyItem` — two-line order, line 1 a legitimate
  own-tenant link, line 2 foreign: asserts `_items.DidNotReceive().Update(...)`, no save, no order.

`MarketplaceServiceTests.cs` — the two metrics-recalc facts replaced by three that assert
`UpsertMetricsRatingAsync(supplierId, supplier.TenantId, avg)` and that the service no longer loads
or stages the metrics row itself (plus the empty-ratings short-circuit).
`SupplierCabinetServiceTests.cs` — three `GetReviewByIdAsync` stubs/assertions moved to
`SetReviewReplyAsync`; the happy path now also asserts the service does **not** call
`SaveChangesAsync` itself.
`ProviderRlsOverrideContainmentTests.cs` — R4, +2 facts.

Support: `PassThroughProviderRlsOverride.cs` (test double for EF-InMemory repository tests, same
pure pass-through convention as the existing `ITenantSessionOverride` stubs).
`MarketplaceRepositoryPlatformTenantTests.cs` uses it; `SupplierAgreementMarkSignedRlsIntegrationTests.cs`
deliberately wires the **real** `ProviderRlsOverride` (live Postgres — the RLS behaviour is the point).

## Build / test

- `dotnet build ShelfGuard.sln -c Release`: **0 errors, 1 warning** — the pre-existing CS8602 in
  `MarketplaceServiceTests.cs` (line 534 → 550, shifted by added lines above it). **0 new warnings, no EF1002.**
- `dotnet test --filter "FullyQualifiedName~Marketplace"`: **237/237 passed**.
- `dotnet test ShelfGuard.sln`: **2023/2023 passed, 0 failed.** Measured baseline before starting was
  **2014/2014** (not the 1953 in the brief — TASK-627+ had already moved it). Delta = my **+8**
  (6 new `[Fact]` in modified files + 2 in the new containment file, verified by diff) **+1** from a
  concurrent session's `NotificationServiceTests.cs` change that landed mid-task. **Zero regressions.**
- Review criteria: `grep GetDbConnection MarketplaceRepository.cs` → **only the doc-comment mention**,
  zero code hits. `grep -r SetProviderRoleAsync` → only the historical mention in the class doc.

**Build-environment note (not a defect):** `dotnet build` in the default **Debug** configuration
fails with `MSB3021/MSB3027` — a dev `ShelfGuard.Api.exe` (PID 6260) started by a concurrent session
holds `ShelfGuard.Api/bin/Debug/net8.0/*.dll` open. Zero C# diagnostics in that run; the failures are
purely file-copy locks. I did not kill another session's server, so verification was done in
**Release**, which compiles the identical sources. Re-run Debug once that server is stopped.

## Deviations from the plan

- **13 → 12 wrapped call sites.** `GetReviewByIdAsync` is deleted per R1 instead of wrapped, and W2
  absorbs its query. Net bypass surface is 12 reads + 2 composites.
- **`PlanCatalogOutcomeAsync`'s new parameter is placed first** (`Guid clientTenantId, SupplierItem, ...`)
  to match the existing `ExecuteCatalogPlanAsync(Guid clientTenantId, ...)` signature, rather than
  appended. Cosmetic; single call site.
- **Two containment tests instead of one (R4).** The constructor-parameter scan alone would miss a
  type that acquires the interface by field/property injection, so a field scan was added.
- **`AddMetricsAsync` kept** on the interface although W1 leaves it with no production caller. It is
  a staging-only method with no bypass, so it is not the "repurposable escape hatch" R1 targeted, and
  deleting it was outside the brief. Flagged here for the post-implementation review (TASK-645) to
  confirm or remove.

---

# Post-review remediation (TASK-645: C1/C2 + cleanups)

TASK-645 returned **SHIP-WITH-CHANGES** — the fix passed all 12 security criteria; two required
changes plus three non-blocking cleanups. All applied.

## C1 (required) — order-number generation silently depended on the leak

**The finding.** `NextOrderNumberAsync` (`MarketplaceOrderService.cs:614`) derives `MP-{yyyy}-{NNN}`
from `CountForSupplierAsync`, and it runs on the **client** session, after the provider-bypass reads.
Pre-fix the leaked `app.role='provider'` satisfied `marketplace_orders.provider_bypass`, so the count
covered all of that supplier's orders. Post-fix it falls back to `marketplace_orders`' OR-based
`tenant_isolation` (`SupplierTenantId = session OR ClientTenantId = session`), which for a client
session means "orders I am a party to" → the sequence restarts per client, and two clients of one
supplier both get `MP-2026-001`. No unique index on `OrderNumber` ⇒ silent corruption. A
customer-visible identifier scheme was unknowingly resting on the RLS leak.

**Fix applied** — count under the supplier's RLS context via the already-injected primitive, same
pattern as the cross-tenant notification outbox at `:323`/`:366`:

```csharp
private Task<string> NextOrderNumberAsync(Guid supplierTenantId, CancellationToken ct) =>
    _tenantSessionOverride.ExecuteAsync(supplierTenantId, async () =>
    {
        var seq = await _orders.CountForSupplierAsync(supplierTenantId, ct) + 1;
        return $"MP-{DateTime.UtcNow.Year}-{seq:D3}";
    }, ct);
```

Preconditions re-verified from source before applying: `supplierTenantId` is trusted at that point
(resolved via `GetSupplierTenantIdAsync` at `:99`, then gated by the ACTIVE-agreement check at
`:104-106`); the target policy is OR-based on `SupplierTenantId`, so the supplier identity exposes
exactly the intended rows; **no ambient transaction is open** at the call site — pass 2's
`ExecuteCatalogPlanAsync` saves have already completed and this service opens no transaction of its
own, so `TenantSessionOverride`'s `BeginTransactionAsync` cannot throw. The doc comment now records
the whole dependency so it can't be silently reintroduced.

**Regression test** — appended one `[Fact]` to QA's
`MarketplaceProviderBypassScopeRlsIntegrationTests.cs`:
`Order_numbers_stay_sequential_per_supplier_across_two_different_client_tenants`. Seeds one supplier
plus **two** client tenants (each with its own Location, User and Active agreement), places one order
from each, and asserts the numbers differ and are exactly `MP-{year}-001` / `-002`, plus a DB-level
distinct-count check. **Proved non-vacuous:** with C1 temporarily reverted the test fails with
`Assert.NotEqual() Failure … Expected: Not "MP-2026-001", Actual: "MP-2026-001"` — the exact
predicted symptom — and passes once restored (11 s against real Postgres, no soft-skip).

**Knock-on fix in QA's other file.** `MarketplaceOrderCatalogConflictsRlsIntegrationTests.BuildOrderService`
passed `Substitute.For<ITenantSessionOverride>()`. Once C1 routes the order number through it, a bare
substitute returns a null `Task<string>` result → null `OrderNumber` → NOT NULL violation on insert,
breaking QA's `CreateOrder_..._provisions_exactly_one_own_tenant_item`. Swapped for the real
`TenantSessionOverride(db)`, which is the correct wiring for a live-RLS suite anyway.
The unit suite needed the matching pass-through stub for the new `Func<Task<string>>` overload in
`MarketplaceOrderServiceTests`' constructor, plus one assertion on the happy path that the count runs
under `_supplierTenantId`.

## C2 (required) — containment test scanned too few assemblies

`ProviderRlsOverrideContainmentTests` covered only `ShelfGuard.Application` + `ShelfGuard.Infrastructure`.
`ShelfGuard.Tests` already references `ShelfGuard.Api`, and `MarketplaceChatController` is live
precedent for a controller injecting a repository directly — so a controller taking
`IProviderRlsOverride` would have passed the very test R4 exists to prevent. Both scans now share one
`ScannedAssemblies` field that includes `ShelfGuard.Api` (via
`typeof(ShelfGuard.Api.Controllers.MarketplaceChatController).Assembly`), with a remark explaining
why `ShelfGuard.Domain` is deliberately absent (it cannot reference Application, so the type is
unreachable there by construction). Refactoring to a shared field also removes the duplicate-array
drift that caused the gap.

## Non-blocking cleanups (both applied)

- **`AddMetricsAsync` deleted** from `IMarketplaceRepository` + `MarketplaceRepository` (zero
  production callers since W1 moved to `UpsertMetricsRatingAsync`), and the now-vacuous
  `DidNotReceive().AddMetricsAsync(...)` assertion dropped from `MarketplaceServiceTests`. This
  resolves the open item I had flagged in my own "Deviations" section above — the reviewer's call was
  to delete, and I agree: unused public surface on the interface this task hardens is the wrong default.
- **Foreign-tenant entities detached (R3).** `UpsertMetricsRatingAsync` now detaches the
  `SupplierMetrics` row after its flush; `SetReviewReplyAsync` detaches both the `SupplierReview` and
  its `.Include`d `Tenant`. Detaching does not clear loaded values, so `SetReviewReplyAsync`'s caller
  still reads `ReplyText`/`RepliedAt`/`Tenant.Name` off the returned instance. Removes the
  "foreign row lingering in the shared change tracker" hazard class outright instead of relying on it
  staying `Unchanged` (which fails closed, but only by luck).

**R4 (blast-radius wording) not actioned here** — it is a doc change to ADR-035 / KI-036 /
`IProviderRlsOverride`'s XML doc, owned by TASK-646. Worth carrying over verbatim: migration
`20260830143000` (concurrent session) takes the `provider_bypass` count **107 → 109**, so the figure
should be phrased as "107 measured 2026-08-30, and growing with every new RLS table" rather than as a
fixed number. R6 is informational and pre-existing.

## Re-verification after remediation

- `dotnet build ShelfGuard.sln -c Release`: **0 errors, 1 warning** — still exactly the pre-existing
  CS8602 at `MarketplaceServiceTests.cs:550`. No new warnings, no EF1002.
- `dotnet test -c Release --filter "…Marketplace|…ProviderRlsOverrideContainment"`:
  **248/248 passed, 0 skipped** (50 s — the RLS facts really hit Postgres).
- `dotnet test ShelfGuard.sln -c Release`: **2037/2037 passed, 0 failed, 0 skipped.** Against the
  2034 baseline that is my **+1** (the C1 regression fact) plus **+2** from the concurrent session's
  notifications work, whose test file grew from +1 to +4 added facts during this window. Zero
  regressions; every delta is accounted for.
- C1 falsification check recorded above (fails without the fix, passes with it).

Still uncommitted. Debug-configuration builds remain blocked by the other session's running
`ShelfGuard.Api.exe` file lock (unchanged, not a code issue).

## Not touched (per brief)

`.claude/docs/` (TASK-646), Part C files/migrations (TASK-642), the two RLS integration test files
(TASK-644), `frontend/`, `mobile/`. Nothing committed. A concurrent session's unrelated notifications
work (`NotificationsController`, `NotificationService`, `frontend/features/notifications/*`,
`CustomerMessageForm.tsx`, `mobile/*`) was left strictly alone.
