# TASK-570 — Catalog Curation (Phase 1): Architecture + Task Breakdown

**Status:** done
**Agent:** project-architect

ADR: `.claude/docs/decisions.md` ADR-032. Doc updates: `.claude/docs/domain-model.md` (Block
Registry section — new `productIds` prop kind). `architecture.md` deliberately not touched — no
layer/module/service boundary change, see ADR-032 Consequences. Task registrations:
`.claude/tasks/current.md`. Handoff: `.claude/logs/handoffs/570-to-backend-mobile-frontend_project-architect.md`.

## What this is

Phase 1 of a larger, deliberately-descoped ask: let a retailer admin curate WHICH specific products
a `productGrid`/`productCarousel` block shows, instead of "first N alphabetically" (today's only
option, via `limit`). Bestsellers, personalization, personal discounts, and the POS-payment bonus
were raised in the same original request and explicitly deferred by the user to a separate future
initiative — nothing here designs, scaffolds, or leaves a placeholder for any of them.

Full reasoning: ADR-032. Summary of the four decisions it makes:

1. **New `BlockPropTypes.ProductIds` kind** (not a `stringArray` + name special-case) — keeps
   `BlockPropertyEditor.tsx`'s "switch only on `def.type`" invariant intact.
2. **Resolution semantics**: curated selection (in admin's chosen order) overrides the alphabetical
   fallback; `limit` becomes a cap on the curated list, not a page-size driver; a stale/deleted
   product id is silently skipped (matches this feature area's existing precedent — nothing here
   ever renders a "no longer exists" placeholder). Empty/absent selection = byte-identical to
   today's behavior, always.
3. **A real correctness gap that would otherwise ship silently**: both `PageRenderer.tsx` (mobile,
   `page=1,pageSize=30`) and `AppPreviewPanel.tsx` (web, `/api/items` default `pageSize=50`, no
   search/id filter at all) only ever fetch a short alphabetical prefix of the catalog. A curated
   pick outside that window would incorrectly resolve as "not found." Fixed with a new bounded
   catalog-by-ids read path on both sides, used only when a page actually has a curated selection.
4. **New `ProductPickerField.tsx`** — no existing component supports "search a tenant's live catalog
   by name, multi-select, ordered." `PromoProductsSection.tsx`'s single native `<select>` doesn't
   fit; a small new component is genuinely required.

## New prop table (source of truth — mirrored by hand on both mobile and frontend, no shared package)

| Block type | Prop | Type | Default | MinItems | MaxItems |
|---|---|---|---|---|---|
| `productGrid` | `productIds` | `productIds` | `[]` | 0 | 30 |
| `productCarousel` | `productIds` | `productIds` | `[]` | 0 | 20 |

`MaxItems` mirrors each type's existing `limit.Max` — an admin can never usefully select more than
could ever display. `promotionGrid`/`promotionCarousel` are **untouched** — out of scope (they
already resolve from `ctx.promotions`, a separately-curated data source).

## Resolution algorithm (must be byte-identical in `resolveBlocks.ts` and `blockPreviews.tsx`)

```
productIds = array of strings from props.productIds, defensively filtered, default []
if productIds.length > 0:
    resolved = for each id in productIds (in this exact order):
                 look up id in the available catalog data (see catalog-by-ids below)
                 skip silently if: not found, OR found but priceRetail === null
    items = resolved.slice(0, limit)
else:
    items = ctx.catalog.filter(item => item.priceRetail !== null).slice(0, limit)   # unchanged today's behavior
```

## New backend read path (both sides need this — see ADR-032 Decision 3)

**Consumer (mobile):** `GET /api/consumer/{tenantId}/catalog/by-ids?storeId=&ids=<guid>&ids=<guid>...`
— same DTO/availability shape as the existing paginated `GET /api/consumer/{tenantId}/catalog`,
bounded to ≤30 ids, `[RequireConsumerFeature("catalog")]` gated (same as the existing endpoint).

**Admin (web preview + picker):** `/api/items` gains two optional query params:
- `search` (name `ILike`, mirrors `ConsumerContentRepository.GetCatalogPagedAsync`'s existing
  pattern) — makes the picker's search-as-you-type possible at all (today `/api/items` has zero
  text search).
- `ids` (repeated `Guid`) — when present, ignore `page`/`pageSize`/other filters, return exactly
  those items (server-clamped to ≤30). Used by `AppPreviewPanel.tsx` to resolve curated selections
  outside its default `pageSize=50` window.

Zero behavior change for every existing caller when neither param is passed.

## Task breakdown

Dependency graph:
```
TASK-571 (backend) ─┬─► TASK-573 (mobile)         [independent branch, contract-driven]
TASK-572 (backend) ─┤
                     └─► TASK-574 (frontend) ─► TASK-575 (frontend) ─┐
TASK-573 ────────────────────────────────────────────────────────────┼─► TASK-576 (qa)
                                                                       ┘
```
Three parallel spawns: **backend-developer** (TASK-571 then 572, sequential, one spawn — see note
below), **mobile-developer** (TASK-573), **frontend-developer** (TASK-574 then 575, sequential, one
spawn). All three can start immediately using the contract fixed in this document — none needs to
wait for another's PR to merge, same precedent as TASK-560→561/562/563. `backend/`, `mobile/`,
`frontend/` are disjoint trees, no worktree isolation needed between the three spawns.

**Why TASK-571/572 are sequential in one backend spawn, not two parallel ones**: their files are
genuinely disjoint (`BlockRegistry.cs`/`BlockPropTypes.cs` vs. `ItemsController.cs`/
`ConsumerContentController.cs` and friends), but this repo has already hit a real incident where two
agents editing disjoint files in the same working tree broke each other's `dotnet build` mid-edit
(shared solution build state, not a file conflict) — see memory `feedback-parallel-ef-migrations-need-worktree`.
Same precaution as TASK-561's own backend task, applied preventively here even though the files
don't overlap.

---

### TASK-571 — Backend: `productIds` block-prop kind + Block Registry entries
**Agent:** backend-developer

**File:** `backend/ShelfGuard.Application/Features/MobileConfig/BlockRegistry/BlockPropTypes.cs`
Add a 7th constant:
```csharp
/// <summary>
/// An array of Item (product) UUIDs the admin explicitly curated, in display order (TASK-570/571,
/// ADR-032 "Catalog Curation"). Same wire shape as <see cref="StringArray"/> (an array of strings)
/// but a distinct kind, not StringArray + a name special-case: the valid values are the tenant's
/// live catalog, not a static AllowedValues list, so AllowedValues stays null here and the admin UI
/// needs an async search-by-name picker (frontend/features/consumer-app/components/
/// ProductPickerField.tsx) instead of StringArrayField's fixed-badge/free-text modes.
/// </summary>
public const string ProductIds = "productIds";
```
Update the class's own doc-comment reference to "the same six kinds" if present elsewhere — check
for other "6 kinds"/"six cases" phrasing in this file's remarks and update to seven for accuracy.

**File:** `backend/ShelfGuard.Application/Features/MobileConfig/BlockRegistry/BlockRegistry.cs`
Add one `BlockPropDefinition` to two `Props` lists (append at the end of each, matching this file's
existing call shape):
```csharp
// productCarousel's Props list — after "cardWidthPx":
new("productIds", BlockPropTypes.ProductIds, Required: false, Default: new List<string>(),
    MinItems: 0, MaxItems: 20),

// productGrid's Props list — after "columns":
new("productIds", BlockPropTypes.ProductIds, Required: false, Default: new List<string>(),
    MinItems: 0, MaxItems: 30),
```
Do **not** touch `promotionGrid`/`promotionCarousel` (out of scope, see ADR-032). Do **not** touch
`MobileConfigWhitelists.cs` or `MobileConfigValidator.cs` — block `props` stays free-form JSON at
save-time by this registry's own already-documented decision (see this file's class remarks); a new
registry entry only changes what `GET /api/v1/mobile/blocks` advertises.

**File:** `backend/ShelfGuard.Tests/MobileConfig/BlockRegistryTests.cs`
Existing generic theory tests already cover the new prop (type/name non-empty, `DefaultProps`
derivation). Add one focused test protecting the exact table above:
```csharp
[Theory]
[InlineData("productCarousel", 20)]
[InlineData("productGrid", 30)]
public void Curatable_product_block_types_declare_productIds_with_expected_MaxItems(
    string blockType, int expectedMaxItems)
{
    var def = BlockRegistry.Definitions.Single(d => d.Type == blockType);
    var prop = def.Props.Single(p => p.Name == "productIds");
    Assert.Equal(BlockPropTypes.ProductIds, prop.Type);
    Assert.Equal(0, prop.MinItems);
    Assert.Equal(expectedMaxItems, prop.MaxItems);
    Assert.False(prop.Required);
    Assert.Null(prop.AllowedValues);
}

[Fact]
public void PromotionCollection_block_types_do_not_get_productIds()
{
    foreach (var type in new[] { "promotionGrid", "promotionCarousel" })
    {
        var def = BlockRegistry.Definitions.Single(d => d.Type == type);
        Assert.DoesNotContain(def.Props, p => p.Name == "productIds");
    }
}
```

**Acceptance criteria:**
- `dotnet build` clean, `dotnet test --filter MobileConfig` full pass including the new tests.
- `GET /api/v1/mobile/blocks` response shows `productIds` in `validationSchema` for `productGrid`/
  `productCarousel` only, with `type: "productIds"`, `allowedValues: null`, correct `maxItems`.
- No changes outside `BlockPropTypes.cs`/`BlockRegistry.cs` + their test file.
- Task log per convention.

---

### TASK-572 — Backend: catalog-by-ids query support (admin `/api/items` + new consumer endpoint)
**Agent:** backend-developer
**Sequenced after TASK-571 in the same spawn** (no file overlap, just the build-fragility precaution
above).

**Part A — Admin `/api/items` gains `search` + `ids` filters**

**File:** `backend/ShelfGuard.Domain/Interfaces/IItemRepository.cs` — extend `GetPagedAsync`'s
signature, inserting `string? search, IReadOnlyList<Guid>? ids` after `managementType`:
```csharp
Task<(List<Item> Items, int Total)> GetPagedAsync(
    Guid? categoryId, Guid? segmentId, string? managementType, string? search, IReadOnlyList<Guid>? ids,
    int page, int pageSize, CancellationToken ct = default);
```

**File:** `backend/ShelfGuard.Infrastructure/Data/Repositories/ItemRepository.cs` — `GetPagedAsync`
(currently lines 40-65): add the two new params, and inside the existing filter chain:
```csharp
if (!string.IsNullOrWhiteSpace(search))
    query = query.Where(p => EF.Functions.ILike(p.Name, $"%{search}%"));
if (ids is { Count: > 0 })
    query = query.Where(p => ids.Contains(p.Id));
```
(Mirrors `ConsumerContentRepository.GetCatalogPagedAsync`'s own existing `ILike` search pattern —
same convention, don't invent a different one.) Leave `.OrderBy(p => p.Name)` and the
Skip/Take unchanged — the controller (below) is responsible for passing a `page`/`pageSize` that
makes sense for the `ids` case.

**File:** `backend/ShelfGuard.Application/Features/Catalog/IItemService.cs` /
`backend/ShelfGuard.Application/Features/Catalog/ItemService.cs` — same signature extension,
thin passthrough (mirrors `ItemService.GetPagedAsync`'s current lines 26-43 shape exactly).

**File:** `backend/ShelfGuard.Api/Controllers/ItemsController.cs` — extend `GetAll` (lines 19-35):
```csharp
[FromQuery] string? search = null,
[FromQuery(Name = "ids")] Guid[]? ids = null,
```
When `ids` is non-empty: clamp to the first 30 (`ids.Take(30).ToArray()`) and force
`page = 1, pageSize = 30` regardless of the incoming `page`/`pageSize` query values — the caller's
intent with `ids` is "give me exactly these," not a paginated browse. Pass `search`,
`ids?.Take(30).ToList()` through to the service call.

**Part B — New consumer endpoint: catalog by exact id list**

**File:** `backend/ShelfGuard.Application/Features/ConsumerContent/IConsumerContentRepository.cs`
Add:
```csharp
/// <summary>Active Items matching exactly the given ids (bounded, ≤30), same shape/store-availability
/// annotation as GetCatalogPagedAsync — resolves a curated productIds selection (TASK-570/572,
/// ADR-032) regardless of where those ids fall in the default alphabetical page window.</summary>
Task<IReadOnlyList<ConsumerCatalogItemDto>> GetCatalogByIdsAsync(
    Guid tenantId, Guid storeId, IReadOnlyList<Guid> ids, CancellationToken ct = default);
```

**File:** `backend/ShelfGuard.Infrastructure/Data/Repositories/ConsumerContentRepository.cs`
Implement `GetCatalogByIdsAsync` — same body shape as `GetCatalogPagedAsync` (lines 84-124: `IsActive`
filter, `.Include(i => i.Category)`, the same stock-availability lookup/join) but filtered by
`ids.Contains(i.Id)` instead of `search`/`categoryId`, and no pagination (return everything that
matches — the caller already bounds `ids.Count` to ≤30).

**File:** `backend/ShelfGuard.Application/Features/ConsumerContent/IConsumerContentService.cs` /
`ConsumerContentService.cs` — thin wrapper `GetCatalogByIdsAsync(tenantId, storeId, ids, ct)`, same
error-handling shape as this service's existing `GetCatalogAsync`. Defensively clamp `ids` to the
first 30 here too (defense in depth — the controller already clamps, but the registry's own
`MaxItems` bound is the real source of truth for "why 30").

**File:** `backend/ShelfGuard.Api/Controllers/ConsumerContentController.cs` — new action, placed
after the existing `GetCatalog`:
```csharp
/// <summary>Active catalog items matching exactly the given ids — resolves a curated productIds
/// selection regardless of alphabetical position (TASK-570/572, ADR-032).</summary>
[HttpGet("{tenantId:guid}/catalog/by-ids")]
[RequireConsumerFeature("catalog")]
public async Task<IActionResult> GetCatalogByIds(
    Guid tenantId, [FromQuery] Guid storeId, [FromQuery(Name = "ids")] Guid[] ids, CancellationToken ct)
{
    if (ids.Length == 0) return Ok(Array.Empty<ConsumerCatalogItemDto>());
    var items = await _service.GetCatalogByIdsAsync(tenantId, storeId, ids.Take(30).ToList(), ct);
    return Ok(items);
}
```
Same `[AllowAnonymous]` (class-level, unchanged) + `[RequireConsumerFeature("catalog")]` gate as the
existing paginated endpoint — curated content is still "catalog" feature content, no new flag.

**Tests:** extend the existing `ItemsController`/`ItemService` test files with `search`/`ids` cases
(name match is case-insensitive per `ILike`; `ids` returns exactly the matching set regardless of
name; `ids` with an id not in the tenant's catalog is simply absent from the result, not an error).
Extend `ConsumerContentService`/`ConsumerContentRepository` test coverage with: empty `ids` → empty
array (not 404); `ids` containing a deactivated item's id → excluded from the result; `ids` list
>30 → truncated server-side.

**Acceptance criteria:**
- `dotnet build` clean, `dotnet test` full pass including new cases.
- `GET /api/items` with no `search`/`ids` behaves byte-identically to today (regression guard).
- `GET /api/items?search=молок` returns only name-matching items (`ILike`, partial, case-insensitive).
- `GET /api/items?ids=<a>&ids=<b>` returns exactly those two items regardless of their alphabetical
  position or the default page size.
- `GET /api/consumer/{tenantId}/catalog/by-ids?storeId=&ids=...` returns the same DTO shape as the
  existing paginated catalog endpoint, `IsActive`-filtered, gated by the `catalog` feature flag same
  as its sibling endpoint.
- Task log per convention.

---

### TASK-573 — Mobile: curated-selection resolution
**Agent:** mobile-developer
**Depends on:** TASK-571 (prop name/bounds) + TASK-572 (by-ids endpoint contract) — not a runtime
dependency on either PR merging; this task can start immediately using the contract fixed above and
the endpoint shape fixed in TASK-572's brief, same precedent as TASK-562 starting before TASK-561
merged. Real end-to-end verification (not unit tests) needs TASK-572 actually deployed/running.

**File:** `mobile/features/consumer-content/types.ts` — no new type needed; the by-ids endpoint
returns the same `ConsumerCatalogItem[]` shape already defined here.

**File:** `mobile/features/consumer-content/api.ts` — add, mirroring `getConsumerCatalog`'s existing
shape (lines 104-119) and `personalApiClient.get<T>(url, { params })` axios-style call convention:
```ts
export async function getConsumerCatalogByIds(
  context: ConsumerContentContext,
  ids: string[]
): Promise<ConsumerCatalogItem[]> {
  if (ids.length === 0) return [];
  const { data } = await personalApiClient.get<ConsumerCatalogItem[]>(
    `/consumer/${context.tenantId}/catalog/by-ids`,
    { params: { storeId: context.storeId, ids } }
  );
  return data.map((item) => ({ ...item, imageUrl: resolveApiAssetUrl(item.imageUrl) }));
}
```
**Verify** `personalApiClient`'s configured axios `paramsSerializer` actually emits repeated
`ids=<guid1>&ids=<guid2>` (ASP.NET Core's `[FromQuery(Name = "ids")] Guid[]` model binder expects
that shape, not `ids[]=`) — if the default serializer doesn't match, build the query string by hand
for this one call rather than relying on axios's array default. Check this against how any other
existing multi-value query param is sent in this codebase first, if one exists.

**File:** `mobile/features/consumer-content/hooks.ts` — add, mirroring `useConsumerCatalog`'s shape:
```ts
export function useConsumerCatalogByIds(context: ConsumerContentContext | null, ids: string[]) {
  return useQuery({
    queryKey: ['consumer-content', 'catalog-by-ids', context?.tenantId, context?.storeId, [...ids].sort()],
    queryFn: () => getConsumerCatalogByIds(context as ConsumerContentContext, ids),
    enabled: Boolean(context) && ids.length > 0,
    staleTime: 60_000,
  });
}
```

**File:** `mobile/features/server-driven-ui/PageRenderer.tsx`
1. Move `const page = config.pages[pageKey];` to run before the other data hooks (still before the
   `if (!page) return null;` early return — hooks must not be conditional).
2. Add a small helper (co-located in this file or a new tiny module) that scans `page?.blocks ?? []`
   for `type === 'productGrid' || type === 'productCarousel'` blocks whose `props.productIds` is a
   non-empty array, and unions all string entries across them (dedupe).
3. `const curatedIds = /* the helper above, keyed on page */;`
4. `const catalogByIds = useConsumerCatalogByIds(context, curatedIds);`
5. Build `catalogById`, merging `catalog.data?.items` (existing page=1/pageSize=30 fetch, unchanged)
   with `catalogByIds.data ?? []` into a `Map<string, ConsumerCatalogItem>` keyed by `id`.
6. Pass `catalogById` into `resolvePage`'s data object alongside the existing `catalog` array (both
   present — `catalog` unchanged for the fallback branch, `catalogById` new for the curated branch).

**File:** `mobile/features/server-driven-ui/resolveBlocks.ts`
1. `BlockDataSources` interface: add `catalogById: ReadonlyMap<string, ConsumerCatalogItem>`.
2. Rewrite the `productCarousel`/`productGrid` case per the resolution algorithm in this log's
   "Resolution algorithm" section above:
```ts
case 'productCarousel':
case 'productGrid': {
  const limit = positiveInt(props.limit, block.type === 'productGrid' ? 12 : 10, 30);
  const productIds = Array.isArray(props.productIds)
    ? props.productIds.filter((v): v is string => typeof v === 'string')
    : [];
  const sourceItems = productIds.length > 0
    ? productIds
        .map((id) => data.catalogById.get(id))
        .filter((item): item is ConsumerCatalogItem => !!item && item.priceRetail !== null)
    : data.catalog.filter((item) => item.priceRetail !== null);
  return { ...block, props: { title: props.title, showViewAll: props.showViewAll,
    columns: columns(props.columns),
    cardWidthPx: typeof props.cardWidthPx === 'number' ? props.cardWidthPx : undefined,
    items: sourceItems.slice(0, limit).map((item) => ({ id: item.id, name: item.name,
      price: item.priceRetail as number, unit: item.unit, imageUrl: item.imageUrl ?? undefined })) } };
}
```

**Files:** `mobile/features/server-driven-ui/blocks/types.ts`,
`mobile/features/server-driven-ui/blocks/validators.ts`, `CoreBlocks.tsx` — **no changes needed**.
`productIds` is consumed and stripped during resolution, exactly like `limit` already is; it never
reaches `ProductCollectionProps`/`isProductCollectionProps`/`ProductGridBlock`/`ProductCarouselBlock`.

**File:** `mobile/features/server-driven-ui/__tests__/resolveBlocks.test.ts` — add cases: (a) a
curated selection resolves items in the admin's exact order, not alphabetical; (b) an id with no
match in `catalogById` is silently skipped, remaining items still resolve; (c) an id whose item has
`priceRetail === null` is silently skipped; (d) curated list longer than `limit` is capped to
`limit`; (e) empty/absent `productIds` produces byte-identical output to today (regression guard —
this is the test that would catch a future accidental behavior change to the fallback branch).

**Acceptance criteria:**
- `npm test` (mobile) green, including new cases.
- `npx tsc --noEmit` clean.
- Manually confirm (read the diff) a block with no `productIds` in `props` resolves identically to
  before this task — zero regression on existing saved configs.
- Task log per convention.

---

### TASK-574 — Frontend: `productIds` field type + `ProductPickerField` + catalog search/by-ids hooks
**Agent:** frontend-developer
**Depends on:** TASK-571 (prop type/bounds) + TASK-572 (`search`/`ids` endpoint) — contract-driven,
can start immediately per this brief.

**File:** `frontend/features/consumer-app/types.ts`
`BlockPropType` union (currently `"string" | "int" | "bool" | "enum" | "url" | "stringArray"`): add
`| "productIds"`.

**File:** `frontend/features/catalog/api/catalog.ts`
Extend `catalogApi.getAll`:
```ts
getAll: (params?: { category_id?: string; management_type?: string; search?: string; ids?: string[] }) => {
  const qs = new URLSearchParams();
  if (params?.category_id) qs.set("category_id", params.category_id);
  if (params?.management_type) qs.set("management_type", params.management_type);
  if (params?.search) qs.set("search", params.search);
  if (params?.ids?.length) for (const id of params.ids) qs.append("ids", id);
  const q = qs.toString();
  return api.get<PagedResult<CatalogProductDto>>(`/api/items${q ? `?${q}` : ""}`).then((r) => r.items);
},
```

**File:** `frontend/features/catalog/hooks/useCatalog.ts`
Extend `useCatalogProducts`'s `params` type to accept `search?: string; ids?: string[]` (already
generic `params` passthrough, minimal change). Add a small dedicated wrapper for clarity at call
sites (used by TASK-575's `AppPreviewPanel.tsx`):
```ts
export function useCatalogProductsByIds(ids: string[]) {
  return useQuery({
    queryKey: ["catalog", "by-ids", [...ids].sort()],
    queryFn: () => catalogApi.getAll({ ids }),
    enabled: ids.length > 0,
  });
}
```

**File:** `frontend/features/consumer-app/components/BlockPropertyEditor.tsx`
Three small additions to the file's existing switches:
- `fieldSchemaFor` (line ~149): `case "productIds": return stringArrayFieldSchema(def, t);` — the
  existing helper already handles `def.allowedValues` being `undefined` gracefully (it only adds the
  `.refine()` allowed-values check when `def.allowedValues` is truthy), so no new schema logic needed.
- `coerceValue` (line ~174): combine into the existing `stringArray` case —
  `case "stringArray": case "productIds": if (Array.isArray(value)) return value.filter(...); ...`
  (same body, both kinds coerce identically: array-of-strings or fall back to `def.default`).
- `PropField` (line ~417, "the sole switch in the file"): add
  `case "productIds": return <ProductPickerField {...props} />;`
- Export `FieldProps` (or the minimal slice `ProductPickerField` needs: `def`, `label`, `setValue`,
  `value`, `error`, `t`) if not already exported, for the new component to import.

**New file:** `frontend/features/consumer-app/components/ProductPickerField.tsx`
- Debounced (≈300ms) name-search input → `useCatalogProducts({ search: debouncedQuery })`, filtered
  client-side to `isActive` (mirror `PromoProductsSection.tsx`'s own `activeCatalogProducts` filter
  pattern — an admin should never be able to curate an already-inactive product) and excluding ids
  already in the current selection.
- Results list: thumbnail (or placeholder matching this feature's existing image-fallback
  convention, e.g. `blockPreviews.tsx`'s `ImagePlaceholder`) + name + `priceRetail` per row, click to
  add. Disabled/hidden once `value.length >= def.maxItems` (mirror `StringArrayField`'s existing
  `atMax` pattern).
- Selected list: ordered chips in selection order (= display order — no drag-reorder this phase, per
  ADR-032 Decision 4; remove-and-re-add covers reordering), each with thumbnail+name+price+remove
  button (`X` icon, mirror `StringArrayField`'s existing tag-removal button styling).
- Small caption under an empty selection explaining the fallback, e.g. "Без вибору показуються перші
  товари за алфавітом" (mirrors this file's existing `hintStyle` convention) — helps the admin
  understand why an empty picker still shows something in the live preview.
- Calls `setValue(def.name, newIds, { shouldValidate: true, shouldDirty: true })` on every
  add/remove, same convention as `StringArrayField`'s `addTag`/`removeTag`.

**Acceptance criteria:**
- `npx tsc --noEmit` clean.
- Manually verified in the browser preview tool: searching finds a product NOT in the first page of
  `/api/items` (i.e., proves `search` actually reaches the backend, not a client-side filter over a
  short default fetch); selecting respects `maxItems` (20 for `productCarousel`, 30 for
  `productGrid`); removing a chip updates the form value; the drawer's existing Apply/Cancel
  behavior (TASK-565's `onLiveChange`) is untouched — this field participates in the same live-edit
  wiring as every other field with zero special-casing in `AppBuilderCanvas.tsx`.
- Task log per convention.

---

### TASK-575 — Frontend: `blockPreviews.tsx` + `AppPreviewPanel.tsx` curated-selection parity
**Agent:** frontend-developer
**Depends on:** TASK-574 (same spawn, sequential — new hook/field it introduces) + TASK-572
(`ids` endpoint).

**File:** `frontend/features/consumer-app/components/AppPreviewPanel.tsx`
1. Scan the `blocks` prop for `productGrid`/`productCarousel` blocks' `props.productIds`, union all
   ids referenced on the currently-previewed page.
2. `useCatalogProductsByIds(curatedIds)` (new hook from TASK-574).
3. Build `catalogById: Map<string, PreviewProductItem>` merging the existing `useCatalogProducts()`
   mapping (already used for `ctx.catalog`) with the by-ids result, mapped through the **same**
   DTO→`PreviewProductItem` transform already used for `ctx.catalog` — do not invent a second mapping.
4. Add `catalogById` to the `PreviewContext` object.

**File:** `frontend/features/consumer-app/components/blockPreviews.tsx`
1. `PreviewContext` interface (line ~88): add `catalogById: Map<string, PreviewProductItem>`.
2. `ProductCarouselPreview` (line ~450) / `ProductGridPreview` (line ~471): read
   `block.props.productIds`; when non-empty, resolve via `ctx.catalogById` in the admin's exact
   order, filtering misses — **identical logic** to TASK-573's `resolveBlocks.ts` case (same
   filter-then-`slice(0, limit)` shape); when empty, keep the existing `ctx.catalog.slice(0, limit)`
   fallback exactly as today. This is the ADR-031 "preview must never lie" requirement carried over
   to this feature — the web preview and the real mobile app must resolve a curated selection the
   same way.

**Acceptance criteria:**
- `npx tsc --noEmit` clean.
- Manually verified in the browser preview tool (matches this feature area's established
  verification style — TASK-564/565/567/568 relied on in-browser DOM checks, not a jest suite, for
  this component tree): curating products in the Property Editor updates the preview column live,
  in the chosen order; a curated pick outside the admin catalog's default `pageSize=50` window still
  resolves correctly (proves the `ids` fetch, not just the default `useCatalogProducts()` list, is
  wired in); empty selection shows the same alphabetical-first-`limit` content as before this task.
- Task log per convention.

---

### TASK-576 — QA: Catalog curation regression pass
**Agent:** qa-tester
**Depends on:** TASK-571, 572, 573, 574, 575 all done.

Scope: `/consumer-app/pages` (Catalog page, `productGrid`/`productCarousel` blocks) end-to-end, plus
a real device/emulator check that the web preview and the published mobile app agree.
- Product picker: search finds a product outside the admin catalog's default page window (verify via
  network tab that `search` reaches `/api/items`, not a client-side filter over a short list).
  Selecting respects `MaxItems` per block type (20/30); removing updates the selection; empty
  selection shows the "falls back to alphabetical" hint.
- Curated selection renders in the admin's exact chosen order on both the web preview and (after
  publish) the real mobile app — not alphabetical.
- `limit` caps a curated selection's displayed count (pick more than `limit`, confirm only the first
  `limit` in chosen order render).
- Deactivate a previously-curated product (Items admin) → it silently disappears from both the web
  preview and the real mobile app on next load — no broken card, no console error, no placeholder.
- A curated product with an id NOT in the first-30-alphabetical (mobile) / first-50-default (web
  preview) window still resolves correctly on both — the specific bug this feature had to avoid
  reintroducing (test with a tenant/category that has 30+ active SKUs).
- Old saved block (pre-TASK-571, no `productIds` in `props`) renders at exactly today's
  alphabetical-first-`limit` behavior on both web preview and real mobile app — zero regression.
- `promotionGrid`/`promotionCarousel` unaffected — no `productIds` prop appears in their Property
  Editor, still resolve from `ctx.promotions` exactly as before.
- Save draft → Publish → real mobile app shows exactly what the web preview showed (ADR-031/032
  "not a lie" carryover).
- Regression: existing App Builder flows (TASK-539/540/541/560-569) unaffected.

Report format: bug list (if any) with repro steps, or a clean pass. Per this repo's short-report
convention.
