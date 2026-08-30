# TASK-645 — Post-implementation review: marketplace provider-role RLS-leak fix

**Agent:** security-reviewer · **Model:** opus · **Date:** 2026-08-30 · **Status:** done
**Scope:** analysis only — no production code changed.
**Reviewed:** TASK-643 (impl, Parts A+B) + TASK-644 (RLS integration tests), both uncommitted.
**Against:** plan `snappy-dreaming-hanrahan.md` + TASK-641 threat model §7 (R1–R7).

**VERDICT: SHIP-WITH-CHANGES** — the security fix itself is correct and complete; two changes
required (one functional regression the fix introduces, one test-coverage gap in R4).

---

## Per-criterion table

| # | Criterion | Result |
|---|---|---|
| 1 | Root cause gone — no `GetDbConnection()`, `SetProviderRoleAsync` deleted, no new raw per-request `SET` | **PASS** |
| 2 | All 12 provider-bypass reads inside an `ExecuteAsync` block; search shares one block; images early-return outside; `AsNoTracking()` where planned | **PASS** |
| 3 | No block wraps an outward call | **PASS** |
| 4 | Composites not repurposable as an escape hatch; F10 flush-first contract holds at both call sites | **PASS** (one minor residual, §R3) |
| 5 | R1 — `GetReviewByIdAsync` deleted from interface + impl, no callers | **PASS** |
| 6 | R2/R4 — XML doc contract complete; containment test genuinely enforces it | **CONCERN → required change C2** |
| 7 | R5 — disproved comment at `MarketplaceOrderReceiptService.cs:153-154` rewritten | **PASS** |
| 8 | Part B filters use JWT-derived `clientTenantId`; write-time re-validation present | **PASS** |
| 9 | `ProviderRlsOverride` does not join an ambient transaction; "let EF throw" is real | **PASS** |
| 10 | Concurrent migration `20260830143000` vs `provider_bypass` / `app.role` / marketplace tables | **PASS** (one doc note, §R4) |
| 11 | `AddMetricsAsync` now unused | **CONCERN** — recommend deletion (non-blocking) |
| 12 | Integration tests exercise real RLS, no soft-skip, headline assertion depends on the fix | **PASS** (independently re-run) |
| — | **NEW: order-number generation silently depended on the leak** | **FAIL → required change C1** |

---

## Evidence per criterion

### 1. Root cause — PASS
`grep GetDbConnection backend/**/*.cs` (production): two hits — `MarketplaceRepository.cs:24` (doc
comment naming it as a standing review criterion) and `Program.cs:251` (pre-existing startup-only
KI-028 RLS canary). Zero code hits in `MarketplaceRepository`. `SetProviderRoleAsync`: zero
references outside three doc comments. Repo-wide sweep for `SET ROLE` / `SET app.` /
`ExecuteSqlRaw*` in production code returns only the three sanctioned override primitives
(`AnalyticsRlsOverride`, `TenantSessionOverride`, the new `ProviderRlsOverride`) and
`TenantConnectionInterceptor`'s connection-open SETs. No new per-request raw mutation introduced.

### 2. Bypass coverage — PASS
`git show HEAD:...MarketplaceRepository.cs` confirms exactly **13** pre-fix `SetProviderRoleAsync`
call sites. Post-fix: **12** wrapped (`GetPublicSuppliersAsync` :55, `CountPublicSuppliersAsync` :65,
`GetSupplierByIdAsync` :71, `GetSupplierItemsAsync` :90, `GetSupplierItemImagesByIdsAsync` :110,
`SearchSuppliersAsync` :130, `GetSupplierByRawIdAsync` :290, `GetSupplierTenantIdAsync` :295,
`GetReviewRatingsAsync` :374, `GetReviewsBySupplierAsync` :384, `CountReviewsBySupplierAsync` :400,
`GetMetricsBySupplierIdAsync` :412) + the 13th (`GetReviewByIdAsync`) deleted per R1, its query
absorbed into `SetReviewReplyAsync`. None missed, none added.
- `SearchSuppliersAsync` :130-160 — both dependent queries in ONE block. ✔
- `GetSupplierItemImagesByIdsAsync` :106-108 — `Count == 0` early return **outside** the block. ✔
- `AsNoTracking()` on `GetSupplierByRawIdAsync` :291 and `GetMetricsBySupplierIdAsync` :413. ✔
  Verified safe: the only `GetSupplierByRawIdAsync` consumers read `.Id`/`.TenantId` only
  (`MarketplaceService.cs:78`, `:219-249`); metrics consumers read scalars.

### 3. No outward calls in any block — PASS
All 14 lambda bodies touch only `_db` (and the private, non-executing `BuildPublicQuery`). No
service, repository, `ITenantSessionOverride` or `IAnalyticsRlsOverride` is reachable from inside
any block.

### 4. Composites — PASS
`UpsertMetricsRatingAsync` and `SetReviewReplyAsync` have narrow, purpose-shaped signatures, each
touches exactly one table, and does read + mutate + `SaveChangesAsync` inside the single block.
Neither can express a general "run this under provider role" request.

**F10 caller contract re-verified from source, not from the logs:**
- `MarketplaceService.CreateReviewAsync` flushes the review at `:101-102`, then calls
  `RecalculateRatingAsync` at `:105` (its only caller). `GetSupplierByRawIdAsync` is now
  `AsNoTracking`, `GetTenantBusinessTypeAsync` is a projection, `ReviewExistsAsync` is `AnyAsync` —
  nothing dirty is pending when `UpsertMetricsRatingAsync` runs.
- `SupplierCabinetService.ReplyToReviewAsync` → `ResolveAsync` (`:379-391`) either loads
  `Unchanged` entities or takes the get-or-create branch, which saves itself. Nothing is staged
  afterwards.
- Structural check for hidden stagers: `AppDbContext` has **no** `SaveChanges`/`SaveChangesAsync`
  override and there is **no** `ISaveChangesInterceptor` registered (`DependencyInjection.cs:38`
  adds `TenantConnectionInterceptor` only). So no middleware/audit layer can put a dirty entity in
  the shared tracker behind these two services' backs. The "flushes ANY pending change" property
  cannot bite on either path today.

**Minor residual (not a defect, see R3):** both composites leave the *foreign-tenant* entity
tracked in the shared context after the block commits (`SupplierMetrics`; `SupplierReview` plus its
`.Include`d `Tenant`). They are `Unchanged`, so nothing flushes them; any future misuse fails
**closed** (0 rows → `DbUpdateConcurrencyException`), not open. This is the same class of hazard
`AsNoTracking()` was added elsewhere to remove.

### 5. R1 — PASS
`grep GetReviewByIdAsync backend/` → zero hits anywhere (interface, impl, tests, callers).

### 6. R2/R4 — CONCERN
XML doc on `IProviderRlsOverride` covers every R2/R4 requirement: MarketplaceRepository-only rule
(1), the measured **107-table** `provider_bypass` blast radius with the explicit "full cross-tenant
read AND write, NOT marketplace-tables-only" framing (2), the one-operation / no-outward-call
invariant (3), the ban on nesting inside `ITenantSessionOverride`/`IAnalyticsRlsOverride` (4),
`app.role`-only (5), plus the F7 `DISCARD ALL` note.

`ProviderRlsOverrideContainmentTests` does more than R4 asked (constructor scan **and** field scan —
the field scan also catches auto-properties via their backing fields) and its `Assert.Equal` on an
ordered `FullName` list genuinely fails if any additional type appears.

**But it scans only two assemblies** — `ShelfGuard.Application` and `ShelfGuard.Infrastructure`
(`ProviderRlsOverrideContainmentTests.cs:34-38`, `:61-65`). `ShelfGuard.Api` is not scanned, and
`ShelfGuard.Tests.csproj` already `ProjectReference`s it. This is not hypothetical: `ShelfGuard.Api`
already contains a controller that injects a repository directly
(`MarketplaceChatController.cs:24-26` takes `IMarketplaceRepository`), so "a controller takes
`IProviderRlsOverride`" is exactly the mistake this test exists to catch, and it would pass. → **C2**.

### 7. R5 — PASS
`MarketplaceOrderReceiptService.cs:153-166` rewritten. The new text states the invariant is false as
a general rule, that it holds on this request only by call ordering (citing TASK-641 §6 F6), that
the real ownership guarantee is the receipt's own `ClientTenantId` check, and names the exact check
to add if a supplier-side lookup is ever hoisted above that line. Fully satisfies R5.

### 8. Part B — PASS
Traced end to end. `MarketplaceCooperationController.ResolveTenantId()` (`:405-409`) reads the
`tenant_id` **JWT claim** only. It is the sole source at `CreateOrder` (`:132`) and
`CheckOrderConflicts` (`:166`). Threaded as `clientTenantId` into
`CreateOrderAsync` → `PlanCatalogOutcomeAsync(clientTenantId, …)` (`:125`, `:439`) and
`ExecuteCatalogPlanAsync(clientTenantId, …)` (`:151`, `:493`). Never read from
`CreateMarketplaceOrderDto` / `CheckMarketplaceOrderConflictsDto`.

All three filters present and correct:
- `MarketplaceOrderService.cs:224` — `matches.FirstOrDefault(m => m.TenantId == clientTenantId)`;
  a foreign row cannot reach `MarketplaceOrderConflictingItemDto` (`:228-230`).
- `:453` — `linkedItem is null || linkedItem.TenantId != clientTenantId` → the **same**
  `LinkedItemNotFoundError` as a genuine miss (existence not revealed).
- `:466-468` — collision set filtered to `TenantId == clientTenantId`.
- **Write-time re-validation present and correct**: `ExecuteCatalogPlanAsync` `:501-502` checks
  `plan.LinkedItem!.TenantId != clientTenantId` and returns before `_items.Update` (`:505`) — a
  genuinely independent second check, since pass 1 and pass 2 are separated by a loop.

### 9. Ambient transactions — PASS
`ProviderRlsOverride.ExecuteAsync` calls `BeginTransactionAsync` unconditionally (`:23`); EF throws
`InvalidOperationException` if the context already has one — this is the intended loud failure and
mirrors `AnalyticsRlsOverride:19`. Re-verified all 5 `ITenantSessionOverride.ExecuteAsync` lambdas
myself (not from the logs): `MarketplaceOrderService.cs:329`/`:372`, `SupplierAgreementService.cs:394`,
`SupplierChatService.cs:152`, `MarketplaceOrderReceiptService.cs:298`. None reaches
`IMarketplaceRepository`. The one that needed real checking —
`MarketplaceOrderReceiptService.cs:298-309`, which calls `_supplierSupport.CreateSystemTicketAsync`
→ `SupplierSupportService.ToDtoAsync` — is clean: `SupplierSupportService`'s only `_marketplace.`
use is `:59` (outside every block), and `ToDtoAsync` (`:222-228`) reaches only `_tenantNames` and
`_orders`. The receipt service's own bypass call (`:369` `GetSupplierItemImagesByIdsAsync`) is
reached from `ToDtoAsync` at `:313`, **after** the block closes. No nesting path exists today.

### 10. Concurrent migration `20260830143000` — PASS (with a doc note)
`AddCustomerMessageCampaignSnapshots` only creates `customer_message_campaigns` and
`customer_message_recipients` with the standard `tenant_isolation` + `provider_bypass` +
`worker_bypass` trio (same `IN ('provider','provider_admin')`, `WITH CHECK` NULL shape). It touches
no marketplace table and changes no `app.role` semantics. **No interaction with this fix.**
Side effect worth recording: once applied it raises the `provider_bypass` count **107 → 109**, so
the hard-coded figure in `IProviderRlsOverride`'s doc and in the containment test's doc is already
dated. → **R4** below.

### 11. `AddMetricsAsync` — recommend deletion (non-blocking)
**One-line verdict: delete it.** It has zero production callers after W1 moved
(`IMarketplaceRepository.cs:175`, `MarketplaceRepository.cs:417`), and its only remaining reference
is `MarketplaceServiceTests.cs:209`'s `DidNotReceive().AddMetricsAsync(...)` assertion, which is
vacuous once the method is dead. It is genuinely harmless (staging-only, no bypass) so this is
cleanup rather than a blocker — but leaving dead public surface on the exact interface this task
exists to harden is the wrong default.

### 12. Integration tests — PASS (independently re-run)
Real harness confirmed by reading the files: `[Collection("TENANT_ISOLATION_TESTS")]`,
`RlsAuditRoleFixture` (creates `rls_audit_test_role` **NOSUPERUSER NOBYPASSRLS**),
`NpgsqlDataSourceBuilder(...).EnableDynamicJson()`, real `ProviderRlsOverride` +
real `MarketplaceRepository`/`ItemRepository`/`ItemService` (NSubstitute only for non-RLS
collaborators), sessions opened with `SET ROLE rls_audit_test_role; SET app.tenant_id=…;
SET app.role='store_manager'`.

**My own run** (`dotnet test -c Release --no-build --filter
"…MarketplaceOrderCatalogConflictsRls|…MarketplaceProviderBypassScope|…ProviderRlsOverrideContainment"`):
**12/12 passed, 0 skipped**, individual RLS facts taking 2–10 s each — i.e. they really hit
Postgres, no soft-skip. Also re-ran the full `dotnet build ShelfGuard.sln -c Release`: **0 errors,
1 warning** (the pre-existing CS8602 at `MarketplaceServiceTests.cs:550`) — matches the TASK-641
baseline, no new warnings, no EF1002.

**Does the headline assertion depend on the fix?** Yes, and it is not only `Assert.Empty(conflicts)`
(which Part B alone would satisfy). Two assertions in the same test isolate **Part A** specifically:
`Assert.Equal("store_manager", await CurrentRoleAsync(session.Db))` on the same still-open
connection, and `Assert.Empty(await new ItemRepository(session.Db).GetByAnyBarcodeAsync([Barcode]))`
— a direct repository call with no app-level filter, which returns the foreign row iff the role
leaked. QA's recorded pre-fix output (`conflicts.Count = 1`, `app.role … = 'provider'`) is
consistent with this test logic. Sanity check passes.

---

## NEW FINDING — required change C1

### Order-number generation silently depended on the leak

**Where:** `MarketplaceOrderService.cs:615-619` → `MarketplaceOrderRepository.cs:62-63`

```csharp
// MarketplaceOrderService.cs:614  — "«MP-{yyyy}-{NNN}» — NNN sequential per supplier"
private async Task<string> NextOrderNumberAsync(Guid supplierTenantId, CancellationToken ct)
{
    var seq = await _orders.CountForSupplierAsync(supplierTenantId, ct) + 1;
    return $"MP-{DateTime.UtcNow.Year}-{seq:D3}";
}
// MarketplaceOrderRepository.cs:62
_db.MarketplaceOrders.CountAsync(o => o.SupplierTenantId == supplierTenantId, ct);
```

**Why the fix changes its behaviour.** It is called at `CreateOrderAsync:158`, i.e. on the **client**
session, *after* the two provider-bypass reads at `:99` and `:109`. Pre-fix the leaked session-level
`app.role='provider'` satisfied `marketplace_orders.provider_bypass`, so the count covered **all** of
that supplier's orders. Post-fix it runs under the client's own ambient RLS, and
`marketplace_orders.tenant_isolation` is OR-based —
`"SupplierTenantId" = session OR "ClientTenantId" = session`
(`Migrations/20260706155440_SupplierCooperation.cs:341-343`) — so a client session sees only the
orders **it** is a party to.

**Effect.** `MP-{yyyy}-{NNN}` stops being "sequential per supplier" (the method's own doc comment at
`:614` states that as the contract) and becomes sequential per *(supplier, client)* pair. Two
different clients of the same supplier each get `MP-2026-001`. There is **no unique index** on
`MarketplaceOrder.OrderNumber` (`AppDbContext.cs:2024` — `HasMaxLength(50).IsRequired()` only), so
this fails **silently**: duplicate, customer-visible order numbers in the supplier cabinet order
list, in the client's `my-orders`, and in the auto-opened discrepancy ticket subject
(`MarketplaceOrderReceiptService.cs:300`).

**Not a security regression** — it is a narrowing, in the fail-closed direction. It is a functional
regression introduced by this change, and it is invisible to the current suite: both new RLS files
seed a single client tenant, and the unit tests mock `IMarketplaceOrderRepository`.

**Suggested fix** (minimal, uses a primitive already injected into this service):

```csharp
// MarketplaceOrderService.cs:615
private Task<string> NextOrderNumberAsync(Guid supplierTenantId, CancellationToken ct) =>
    _tenantSessionOverride.ExecuteAsync(supplierTenantId, async () =>
    {
        var seq = await _orders.CountForSupplierAsync(supplierTenantId, ct) + 1;
        return $"MP-{DateTime.UtcNow.Year}-{seq:D3}";
    }, ct);
```

Safe because: `supplierTenantId` is already a trusted value at `:158` (resolved through the
provider-bypass read at `:99` and gated by the ACTIVE-agreement check at `:104-106`); the target
table's policy is OR-based on `SupplierTenantId`, so the supplier identity makes exactly the
intended rows visible and nothing more; there is no ambient transaction at `:158`
(`ExecuteCatalogPlanAsync`'s saves have already completed in pass 2); and this is the same pattern
already used 5× in this codebase for trusted cross-tenant work. Add a regression test with **two**
client tenants ordering from one supplier and assert distinct `OrderNumber`s.

**Alternative if the team prefers:** accept per-(supplier, client) numbering as the intended
semantics and update the doc comment at `:614` — but that changes a customer-visible identifier
scheme, so it is a product decision, not an implementation detail. A Postgres sequence per supplier
is the clean long-term answer and is out of scope here.

---

## Required changes

- **C1 — fix the order-number regression** (`MarketplaceOrderService.cs:615-619`). Details and
  suggested patch above. Small and self-contained; if the security fix must ship first, land C1 as
  an immediate follow-up task rather than letting it drop.
- **C2 — close the containment-test gap** (`ProviderRlsOverrideContainmentTests.cs:34-38` and
  `:61-65`). Both assembly arrays must also include `ShelfGuard.Api`, e.g. add
  `typeof(ShelfGuard.Api.Controllers.MarketplaceChatController).Assembly` (the test project already
  references `ShelfGuard.Api`). Without it, R4's stated guarantee — "no other type acquires the
  bypass" — is not enforced for the one project layer that already injects repositories into
  controllers.

## Recommended, non-blocking

- **R3** — detach (or project out) the foreign-tenant entity returned by `SetReviewReplyAsync` /
  loaded by `UpsertMetricsRatingAsync` before the block returns, so no foreign row lingers in the
  shared change tracker. Fails closed today; this just removes the class entirely.
- **R4** — in ADR-035 / KI-036 (TASK-646), state the blast radius as "**107** `provider_bypass`
  policies measured 2026-08-30, and growing with every new RLS table" — migration `20260830143000`
  already takes it to 109. Same wording change in `IProviderRlsOverride`'s XML doc.
- **R5** — delete `AddMetricsAsync` from `IMarketplaceRepository` + `MarketplaceRepository`, and
  drop the now-vacuous `DidNotReceive().AddMetricsAsync` assertion at `MarketplaceServiceTests.cs:209`
  (criterion 11).
- **R6** — informational, pre-existing: the RLS suites soft-skip (`if (!_dbAvailable) … return;`)
  rather than failing when Postgres is unreachable. That is the project convention, but it means a
  CI runner without Postgres reports these files as green-and-vacuous. Verified not the case for
  this run (12/12 executed for real).

## Verification I performed

- Read in full: `IProviderRlsOverride.cs`, `ProviderRlsOverride.cs`, `AnalyticsRlsOverride.cs`,
  `MarketplaceRepository.cs`, both new test files, `ProviderRlsOverrideContainmentTests.cs`,
  `PassThroughProviderRlsOverride.cs`, `RlsAuditRoleFixture.cs`; plus the relevant halves of
  `MarketplaceOrderService.cs`, `MarketplaceService.cs`, `SupplierCabinetService.cs`,
  `SupplierAgreementService.cs`, `SupplierSupportService.cs`, `SupplierChatService.cs`,
  `MarketplaceOrderReceiptService.cs`, `MarketplaceOrderRepository.cs`,
  `MarketplaceCooperationController.cs`.
- `git diff` of every file in scope + `git show HEAD:` of the pre-fix `MarketplaceRepository` to
  confirm the 13 → 12+delete mapping. Concurrent-session files (notifications / CustomerMessage /
  frontend / mobile) excluded as briefed.
- Greps: `GetDbConnection`, `SetProviderRoleAsync`, `GetReviewByIdAsync`, `AddMetricsAsync`,
  `SET ROLE`/`SET app.`/`ExecuteSqlRaw`, all `IMarketplaceRepository` consumers, all
  `ITenantSessionOverride.ExecuteAsync` lambdas, all `CountAsync`-based numbering schemes.
- `dotnet build ShelfGuard.sln -c Release` → **0 errors, 1 pre-existing warning**.
- `dotnet test -c Release --no-build --filter "…MarketplaceOrderCatalogConflictsRls|…MarketplaceProviderBypassScope|…ProviderRlsOverrideContainment"`
  → **12/12 passed, 0 skipped** (real Postgres :5435).
- No production code modified by this task.

---

# C1/C2 remediation confirmation (same session, 2026-08-30)

Targeted re-check of the backend-developer's remediation only — not a re-review of the original fix.

**VERDICT: SHIP.** All five points confirmed. No new findings.

| # | Item | Result |
|---|---|---|
| 1 | C1 — `NextOrderNumberAsync` wrapped in `ITenantSessionOverride` | **CONFIRMED** |
| 2 | C1 knock-on — real `TenantSessionOverride` in the RLS suite's `BuildOrderService` | **CONFIRMED** |
| 3 | C1 regression test proves what it claims | **CONFIRMED** |
| 4 | C2 — containment scan now covers `ShelfGuard.Api` | **CONFIRMED** |
| 5 | Cleanups — `AddMetricsAsync` deleted; both composites detach | **CONFIRMED** |

### 1. C1 — `MarketplaceOrderService.cs:611-638`
- **(a) No ambient transaction at the call site.** `NextOrderNumberAsync` is invoked only at
  `CreateOrderAsync:158`, in the `MarketplaceOrder` object initializer — after pass 2's loop
  (`:149-154`) and before `_orders.AddAsync`/`SaveChangesAsync` (`:175-176`). Nothing in that path
  opens a transaction that outlives its own call: `ExecuteCatalogPlanAsync` does plain
  `SaveChangesAsync` / `ItemService.CreateAsync` (EF's implicit per-save transaction, committed
  internally), and the two `ProviderRlsOverride` blocks at `:99`/`:109` committed long before. No
  other caller exists. `TenantSessionOverride:19` would throw `InvalidOperationException` loudly
  otherwise — and the live test now exercises this path end to end, so nesting is disproved
  empirically, not just by inspection.
- **(b) Revert + subsequent INSERT.** `SET LOCAL app.tenant_id` scope ends at
  `tx.CommitAsync` (`TenantSessionOverride:34-39`), so `app.tenant_id` is back to the client for
  `:175-176`. The `marketplace_orders` row carries `ClientTenantId = clientTenantId = session` and
  the policy is OR-based with no explicit `WITH CHECK` (defaults to `USING`), so the INSERT
  satisfies it via the `ClientTenantId` branch; `marketplace_order_items` rows carry the same
  `ClientTenantId` (`:135`) under the same OR policy. Confirmed live — the new test's orders persist
  and are read back.
- **(c) `supplierTenantId` trusted.** Resolved at `:99` from `GetSupplierTenantIdAsync(supplierId)`
  (route id → real `suppliers` row) and gated by the ACTIVE-agreement check at `:104-106`. The
  block body is a single `CountAsync`; only an `int` crosses the boundary, so no other tenant's row
  data escapes, and the "how many orders does this supplier have" inference was already implicit in
  the returned order number.
- **(d) Race.** Count-then-insert is still not atomic — identical to the pre-fix shape and to
  `SupplierAgreementService.NextContractNumberAsync:549`. Pre-existing, not introduced here, and
  correctly out of scope. (A Postgres sequence remains the clean long-term answer.)

### 2. C1 knock-on — `MarketplaceOrderCatalogConflictsRlsIntegrationTests.cs:289-292`
Correct and necessary: a bare `Substitute.For<ITenantSessionOverride>()` returns a null
`Task<string>` from `ExecuteAsync`, so `await` would have thrown before `OrderNumber` was ever set.
Swapping in the real `TenantSessionOverride(db)` masks nothing — it is the production implementation
against live Postgres, which is the whole point of this suite, and the only other
`_tenantSessionOverride` uses in `MarketplaceOrderService` (`:329`, `:372`) are supplier-side methods
these tests never call.

### 3. C1 regression test — `MarketplaceProviderBypassScopeRlsIntegrationTests.cs:330-360`
Logic checks out. Two distinct client tenants, one supplier, each order placed in its own
`OpenSessionAsync` RLS session. It asserts more than "distinct": the exact
`MP-{year}-001` / `MP-{year}-002` pair, which pins *sequential per supplier* rather than merely
*not colliding*. Pre-C1 both calls return `MP-{year}-001`, so `Assert.NotEqual` fails first with
`Expected: Not "MP-2026-001"` — matches the backend agent's report. Independent cross-check via a
plain context (2 rows, 2 distinct numbers) closes the loop. `PlaceOrderAsync` also re-asserts
`app.role == store_manager` after each order.
*Optional strengthening (not required):* it asserts `app.role` but not
`current_setting('app.tenant_id')` after the override; a tenant_id leak would be masked here because
the OR-based policy accepts the INSERT under either identity. Other facts in the file already cover
tenant scoping, so this is a nice-to-have.

### 4. C2 — `ProviderRlsOverrideContainmentTests.cs:31-46`
Both scans now share one `ScannedAssemblies` field including
`typeof(ShelfGuard.Api.Controllers.MarketplaceChatController).Assembly`. A controller taking
`IProviderRlsOverride` as a constructor parameter, or holding it in a field (auto-properties
included, via their backing field), now appears in the computed list and breaks the
`Assert.Equal` against the one-element `AllowedConsumers`. Excluding `ShelfGuard.Domain` is correct
— it cannot reference the Application assembly, so the type is unreachable there by construction.
The gap C2 named is closed.

### 5. Cleanups
- `AddMetricsAsync`: zero code references remain anywhere (`IMarketplaceRepository.cs:175` and
  `MarketplaceServiceTests.cs:207` are explanatory comments only). The vacuous `DidNotReceive`
  assertion is gone.
- `UpsertMetricsRatingAsync` (`MarketplaceRepository.cs:452-456`) detaches the `SupplierMetrics`
  row after `SaveChangesAsync`. `SetReviewReplyAsync` (`:484-490`) detaches the `.Include`d
  `Tenant` first, then the `SupplierReview` — both inside the block. Detaching does not clear
  loaded navigation values, so `SupplierCabinetService.cs:215`'s `review.Tenant?.Name` still
  resolves; `SupplierCabinetServiceTests.cs:223` (`Assert.Equal("Reviewer Co", item.ReviewerName)`)
  covers exactly that and passes. The R3 hazard class is now removed rather than merely benign.

### Verification performed for this confirmation
- Read the remediated `NextOrderNumberAsync`, `CreateOrderAsync:145-180`, `TenantSessionOverride.cs`,
  both composite methods, `ProviderRlsOverrideContainmentTests.cs`, the new regression test and both
  `BuildOrderService` factories.
- `dotnet build ShelfGuard.sln -c Release` → **0 errors** (57 s).
- `dotnet test -c Release --no-build --filter
  "…MarketplaceOrderCatalogConflictsRls|…MarketplaceProviderBypassScope|…ProviderRlsOverrideContainment|…SupplierCabinetServiceTests|…MarketplaceServiceTests"`
  → **89/89 passed, 0 skipped**, including
  `Order_numbers_stay_sequential_per_supplier_across_two_different_client_tenants` (3 s — real
  Postgres, no soft-skip).
- Still no production code modified by this task.
