# TASK-644 — Marketplace provider-RLS leak: real-Postgres RLS integration coverage (qa-tester)

**Agent:** qa-tester · **Model:** sonnet · **Date:** 2026-08-30 · **Status:** done (uncommitted)
**Plan:** `snappy-dreaming-hanrahan.md` §Тести / §Верифікація
**Depends on:** TASK-643 (Parts A+B impl, uncommitted in the working tree) · threat model TASK-641 §7 (R6 additions)
**Not committed. No production source touched — only two new test files added.**

## Deliverables (two new files, `backend/ShelfGuard.Tests/Infrastructure/`)

### `MarketplaceOrderCatalogConflictsRlsIntegrationTests.cs` — headline regression
Drives the real `MarketplaceOrderService` (real `MarketplaceRepository` + `ItemRepository` +
`SupplierAgreementRepository` + `MarketplaceOrderRepository` + `ItemService` + real
`ProviderRlsOverride`; NSubstitute only for `ISupplierChatRepository`/`INotificationRepository`/
`ITenantSessionOverride`) against live dev Postgres :5435 under a real `rls_audit_test_role` client
session (`SET app.tenant_id=<client>; SET app.role='store_manager'`).

Seed per test: `clientTenant` (empty item catalog) + client `User` + client `Location`;
`supplierTenant` + `Supplier` + public `SupplierProfile` + available `SupplierItem` +
`SupplierItemBarcode('BC-644-<run>')`; **Active** `SupplierAgreement` for the pair; a **third**
`tenant` owning an `Item` whose `Barcodes` jsonb contains the same barcode.

| # | Test | Assertions |
|---|---|---|
| 1 | `CheckCatalogConflicts_under_client_session_ignores_foreign_tenant_barcode_and_does_not_leak_provider_role` | `error == null`; not a gate violation; **`conflicts` is empty** (fails pre-fix — see below); on the **same open connection** `current_setting('app.role')` == `'store_manager'` (leak-does-not-survive — highest value); a direct `ItemRepository.GetByAnyBarcodeAsync([barcode])` on that session returns **0 rows** |
| 2 | `CreateOrder_under_client_session_provisions_exactly_one_own_tenant_item` | `CreateOrderAsync` (auto action) succeeds, no `BarcodeCollisionError`; `app.role` back to `store_manager`; via plain ctx exactly **one** `Item` with `TenantId==clientTenant` and `SourceSupplierItemId==supplierItem.Id` and the barcode; third tenant's `Item.SourceSupplierItemId` still null; exactly 1 `marketplace_orders` row for the client |
| 3 | `CheckCatalogConflicts_reports_the_conflict_when_the_client_tenant_itself_owns_the_barcode` | negative control — client tenant also owns an `Item` with the barcode → exactly **1** conflict, `ExistingItem.Id == clientOwnItem.Id`, name/barcode match (over-correction guard) |
| 4 | `CreateOrder_link_to_a_foreign_tenant_item_is_rejected_and_never_writes_to_that_tenant` | **F2 write-vector negative control (R6)** — `CatalogAction="link"`, `LinkedItemId` = third tenant's item → `LinkedItemNotFoundError`; `app.role` back to `store_manager`; via plain ctx third tenant's `Item.SourceSupplierItemId` still null **and** its `categories` row (`Name=='SENTINEL-CAT-644'`) **and** its `suppliers` row (`Name=='SENTINEL-SUP-644'`) byte-unchanged (pre-fix `_items.Update` flushed the whole `.Include`d graph — 4-table rewrite); no `marketplace_orders` row created |

### `MarketplaceProviderBypassScopeRlsIntegrationTests.cs` — primitive + W1/W2
Same harness. Real `MarketplaceRepository(db, new ProviderRlsOverride(db))` / `MarketplaceService` /
`SupplierCabinetService` under real reviewer / supplier / client sessions.

| # | Test | Assertions |
|---|---|---|
| 1 | `GetSupplierItemsAsync_under_a_client_session_reads_cross_tenant_then_reverts_the_role` | client session reads the foreign supplier's `SupplierItem` (bypass works); immediately after, `current_setting('app.role')` == `store_manager`; `db.Items.CountAsync()` on that session == **1** (own tenant only — would be 3 if the role leaked; supplier tenant seeded with 2 `items` rows) |
| 2 | `Raw_set_local_app_role_provider_reverts_when_its_transaction_commits` | mechanism control — raw `BEGIN; SET LOCAL app.role='provider'; COMMIT;` then `current_setting('app.role')` == `store_manager` |
| 3 | `CreateReviewAsync_first_ever_review_inserts_the_cross_tenant_supplier_metrics_row` | **W1 INSERT branch (R6)** — first-ever review, no `supplier_metrics` row exists → `CreateReviewAsync` succeeds; `app.role` reverts; via plain ctx `supplier_reviews` row `TenantId==reviewer` Rating 5; `supplier_metrics` row **created** with `TenantId==supplierTenant`, `Rating==5.00` |
| 4 | `CreateReviewAsync_second_review_from_another_tenant_updates_the_existing_metrics_row` | **W1 UPDATE branch** — reviewer1 rating 4 → metrics inserted `Rating==4.00`; reviewer2 rating 2 → **still exactly one** metrics row, `Rating==3.00` (avg), `TenantId==supplierTenant`; 2 `supplier_reviews` rows; `app.role` reverts |
| 5 | `ReplyToReviewAsync_under_the_supplier_session_persists_a_reply_on_a_foreign_tenant_review` | **W2** — supplier session (owner-managed profile), foreign-tenant review seeded → `SupplierCabinetService.ReplyToReviewAsync` returns `ReplyText` set; `app.role` reverts; via plain ctx `supplier_reviews.ReplyText` set, `RepliedAt` not null, row still `TenantId==reviewer` |
| 6 | `Public_marketplace_reads_still_cross_tenant_under_a_client_session` | positive control — `GetSupplierByIdAsync` / `GetPublicSuppliersAsync` / `GetSupplierItemImagesByIdsAsync` all still return the foreign public supplier's data under a client session; `app.role` reverts |

## Proof the leak fails pre-fix (plan step 5 — mandatory)

`git worktree` is broken on this repo on Windows (MAX_PATH) and a blanket `git stash -u` would sweep
a concurrent session's unrelated notifications/CustomerMessage work + the Part C doc edits, so the
TASK-643 source was reverted by **explicit pathspec** only:

```
git stash push -m "TASK-644-prefix-repro" -- \
  backend/ShelfGuard.Application/Features/Marketplace/{MarketplaceOrderReceiptService,MarketplaceOrderService,MarketplaceService,SupplierCabinetService}.cs \
  backend/ShelfGuard.Domain/Interfaces/IMarketplaceRepository.cs \
  backend/ShelfGuard.Infrastructure/Data/Repositories/MarketplaceRepository.cs \
  backend/ShelfGuard.Infrastructure/DependencyInjection.cs \
  backend/ShelfGuard.Tests/Infrastructure/SupplierAgreementMarkSignedRlsIntegrationTests.cs \
  backend/ShelfGuard.Tests/Marketplace/{MarketplaceOrderServiceTests,MarketplaceRepositoryPlatformTenantTests,MarketplaceServiceTests,SupplierCabinetServiceTests}.cs
```

The two new integration test files + the four untracked new source/test files
(`IProviderRlsOverride.cs`, `ProviderRlsOverride.cs`, `PassThroughProviderRlsOverride.cs`,
`ProviderRlsOverrideContainmentTests.cs`) were moved out to scratchpad for the stash window, and a
throwaway `_Task644PrefixLeakReproTests.cs` drove the **pre-fix** `MarketplaceOrderService.
CheckCatalogConflictsAsync` (pre-fix `MarketplaceRepository(db)` 1-arg ctor) against the same fixture
under a real client RLS session.

### Recorded pre-fix failure output (verbatim)

```
  Failed ShelfGuard.Tests.Infrastructure._Task644PrefixLeakReproTests.CheckCatalogConflicts_should_be_empty_but_is_not_on_prefix_code [5 s]
  Error Message:
   Assert.Empty() Failure: Collection was not empty
Collection: [MarketplaceOrderConflictDto { SupplierItemId = 70691801-1e87-45f0-9583-0a1de3579083, ExistingItem = MarketplaceOrderConflictingItemDto { Id = dc436a5b-dbfb-4451-95c7-763f4feb2486, Name = Чужий товар (третій тенант), ImageUrl = https://example.test/foreign.jpg, Barcodes = System.Collections.Generic.List`1[System.String] } }]
  Standard Output Messages:
 error = <null>
 conflicts.Count = 1
   LEAKED CONFLICT: supplierItemId=70691801-1e87-45f0-9583-0a1de3579083 -> existing Item id=dc436a5b-dbfb-4451-95c7-763f4feb2486, name='Чужий товар (третій тенант)', imageUrl='https://example.test/foreign.jpg', barcodes=[BC-644REPRO-c0b1df0d22614628bde5de77631c4a15]  (thirdTenant item id was dc436a5b-dbfb-4451-95c7-763f4feb2486)
 app.role AFTER the call (never reset pre-fix) = 'provider'
```

Pre-fix, a client tenant with an **empty** catalog got back one conflict disclosing the **third
tenant's** `Item` id / name / imageUrl / barcodes, and `app.role` on the connection was left as
`'provider'` after the call returned — exactly the reported symptom + the root cause.

Then: scratch test deleted, `git stash pop` (clean, dropped `4cca5d6…`), new files restored,
`grep SetProviderRoleAsync MarketplaceRepository.cs` == 1 (doc-comment mention only — matches
TASK-643), test project rebuilt, both new filters re-run green.

## Verification

- `docker compose ps` — `crmproductsystems-postgres-1` up (healthy), port 5435. `pg_policies`
  confirmed live: `items` / `supplier_items` / `supplier_item_barcodes` / `supplier_profiles` /
  `suppliers` / `supplier_metrics` / `supplier_reviews` all carry `provider_bypass`
  (`app.role = ANY('{provider,provider_admin}')`, `WITH CHECK` NULL) + fail-closed `tenant_isolation`.
- **Debug `dotnet build` blocked** by a concurrent session holding `bin/Debug` DLLs (TASK-643's
  known issue) → built + tested in **Release** throughout (identical sources).
- `dotnet build ShelfGuard.Tests -c Release` — 0 errors, **1 warning** (pre-existing CS8602
  `MarketplaceServiceTests.cs:550` — the TASK-641 baseline).
- `dotnet test --filter "FullyQualifiedName~MarketplaceOrderCatalogConflictsRls|FullyQualifiedName~MarketplaceProviderBypassScope"`
  → **Passed 10, Failed 0, Skipped 0** (both files actually executed — no "DB not available — skipped"
  line; Postgres was reachable).
### Full-suite result

`dotnet test ShelfGuard.sln -c Release` (exit 0):

```
Passed!  - Failed: 0, Passed: 2034, Skipped: 0, Total: 2034 - ShelfGuard.Tests.dll (net8.0)
```

- **2034 / 2034 passed, 0 failed, 0 skipped.** Zero "DB not available — skipped" lines — the
  Postgres-backed RLS classes executed for real.
- TASK-643's measured baseline was **2023/2023**. Delta **+11** = this task's **+10** (4 + 6 new
  facts, verified by the two explicit filter runs: `~MarketplaceOrderCatalogConflictsRls` → 4/4,
  `~MarketplaceProviderBypassScope` → 6/6, both 0 skipped) **+1** from the concurrent
  notifications/CustomerMessage session that kept adding tests during this task. **Zero
  regressions.**

## Notes for TASK-645 (post-impl review)

- Nothing in the fix was changed. No bug found in the TASK-643 implementation during test
  authoring — the leak-does-not-survive assertion, the W1 INSERT+UPDATE branches, W2, and the
  public-read positive control all behave correctly under real RLS.
- A concurrent session landed a new migration
  (`20260830143000_AddCustomerMessageCampaignSnapshots.cs` + `AppDbContextModelSnapshot.cs`) mid-task
  — unrelated to this work, left untouched.
