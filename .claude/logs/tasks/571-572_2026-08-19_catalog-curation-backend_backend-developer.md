# TASK-571/572 — Backend: `productIds` block-prop kind + catalog-by-ids query support

**Status:** done
**Agent:** backend-developer

Full brief: `.claude/logs/tasks/570_2026-08-19_catalog-curation-architecture_project-architect.md`
(sections "### TASK-571" / "### TASK-572"), ADR-032. Done sequentially in one session per the brief's
build-fragility precaution (`feedback-parallel-ef-migrations-need-worktree`); `dotnet build`
re-verified clean after each task.

## TASK-571 — `productIds` block-prop kind

- `BlockPropTypes.cs` — added `ProductIds = "productIds"` constant with the exact doc comment from
  the brief. No "six kinds" phrasing existed anywhere in the file/repo to update.
- `BlockRegistry.cs` — added `productIds` (`MinItems: 0, MaxItems: 20`) to `productCarousel`'s Props,
  and `productIds` (`MinItems: 0, MaxItems: 30`) to `productGrid`'s Props. `promotionGrid`/
  `promotionCarousel` untouched (confirmed by reading the diff — both still have exactly their
  original 3 props). `MobileConfigWhitelists.cs`/`MobileConfigValidator.cs` untouched.
- `BlockRegistryTests.cs` — added the two tests from the log verbatim
  (`Curatable_product_block_types_declare_productIds_with_expected_MaxItems`,
  `PromotionCollection_block_types_do_not_get_productIds`). Also had to add `BlockPropTypes.ProductIds`
  to the existing `Every_prop_has_a_known_type_and_non_empty_name` test's hardcoded `knownTypes` array
  — required for that pre-existing generic theory test to keep passing now that `productGrid`/
  `productCarousel` declare a prop of this new type; not called out explicitly in the brief but
  necessary for the suite to build/pass.
- `GET /api/v1/mobile/blocks` wiring not spot-checked live (no server started) — `MobileBlocksController`
  maps `IBlockRegistryProvider.GetAll()` through `BlockDefinitionDto.From` generically with zero
  per-block-type branching, so registry-level test coverage (all passing) is sufficient proof.

## TASK-572 — catalog search/by-ids support

**Part A — admin `/api/items`:** `IItemRepository`/`ItemRepository`/`IItemService`/`ItemService`
`GetPagedAsync` all extended with `string? search, IReadOnlyList<Guid>? ids` (ILike search mirrors
`ConsumerContentRepository.GetCatalogPagedAsync`'s existing pattern; `ids.Contains(p.Id)` filter;
`OrderBy`/Skip/Take untouched). `ItemsController.GetAll` gained `search`/`ids` query params; when
`ids` is non-empty it's clamped to 30 and `page`/`pageSize` are forced to `1`/`30` regardless of the
caller's own values.

Two pre-existing hand-written `IItemRepository` test fakes (`PosServiceTests.FakeCatalogRepo`,
`FiscalizationRetryTests.RetryFakeCatalogRepo`) needed their `GetPagedAsync` signature updated to
match — caught immediately by the build, not a design change.

**Part B — consumer `catalog/by-ids`:** `IConsumerContentRepository.GetCatalogByIdsAsync` added and
implemented in `ConsumerContentRepository` (same `IsActive`/Category/stock-availability shape as
`GetCatalogPagedAsync`, filtered by `ids.Contains` instead of search/category, no pagination).
`IConsumerContentService`/`ConsumerContentService` add a thin wrapper: tenant-existence check first
(same order as this service's other methods), then an empty-ids short-circuit returning
`Array.Empty<>()`, then a defensive clamp to 30 before calling the repo. `ConsumerContentController`
gained `GetCatalogByIds` at `GET {tenantId}/catalog/by-ids?storeId=&ids=...`, gated by
`[RequireConsumerFeature("catalog")]` same as `GetCatalog`; empty `ids` returns `Ok([])` directly
without calling the service.

## Tests added

- `Catalog/ItemServiceTests.cs` — 3 new cases: no search/ids passthrough (regression guard),
  search passthrough, ids passthrough with exact-set result.
- `Catalog/ItemsControllerTests.cs` (new file) — 4 cases: byte-identical no-param behavior, search
  passthrough, ids clamp-to-30 + forced page=1/pageSize=30 overriding an incoming page=3/pageSize=10,
  and a <30-ids case that still forces page/pageSize without truncating.
- `Catalog/ItemRepositoryGetPagedTests.cs` (new file, InMemory EF) — ids filter returns the exact
  set regardless of alphabetical position, a missing id is silently absent (not an error), and a
  no-params call is byte-identical to today (`OrderBy(Name)` unchanged). `search`'s `EF.Functions.ILike`
  is Npgsql-only and not InMemory-testable — same pre-existing gap as `GetCatalogPagedAsync`'s own
  search filter, which also has zero prior test coverage in this repo; the three layers above
  (service/controller tests) prove `search` is wired through unchanged.
- `ConsumerContent/ConsumerContentServiceTests.cs` (new file) — tenant-not-found error, empty-ids
  short-circuit (no repo call), >30-ids server-side truncation before the repo call, and a
  valid-ids happy path.
- `ConsumerContent/ConsumerContentRepositoryGetCatalogByIdsTests.cs` (new file, InMemory EF) —
  deactivated item excluded, unmatched id silently absent, empty ids → empty result, cross-tenant
  item excluded.
- `ConsumerContent/ConsumerContentControllerGetCatalogByIdsTests.cs` (new file) — empty-ids
  short-circuit without calling the service, >30-ids clamp before the service call, tenant-not-found
  → 404, valid-ids happy path.
- `Authorization/ConsumerContentControllerFeatureGateTests.cs` — added `GetCatalogByIds` to the
  existing `[RequireConsumerFeature("catalog")]` reflection theory.

## Verification

- `dotnet build` — clean after TASK-571, clean again after TASK-572 (2 separate re-verifications
  per the brief).
- `dotnet test --filter MobileConfig.BlockRegistryTests` — 48/48 pass.
- `dotnet test` (full suite) — 1738/1738 pass, 0 skipped, 0 failed.
- Confirmed no other `IItemRepository`/`IConsumerContentRepository`/`IConsumerContentService`
  implementers exist besides the two fakes already fixed (grepped the whole backend tree).

One incidental fix: a stale `dotnet run --project ShelfGuard.Api` process from an earlier session was
holding a file lock on the Api project's build output, causing the first `dotnet build` attempt to
fail with `MSB3027`/`MSB3021` copy errors unrelated to this change. Stopped that process before
building; if the user was using that local dev server, it needs restarting.
