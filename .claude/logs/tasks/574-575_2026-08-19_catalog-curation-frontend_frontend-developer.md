# TASK-574 + TASK-575 — Catalog Curation (Phase 1): frontend

**Status:** done
**Agent:** frontend-developer

Full brief: `.claude/logs/tasks/570_2026-08-19_catalog-curation-architecture_project-architect.md`
(sections TASK-574/TASK-575). ADR: `.claude/docs/decisions.md` ADR-032.

## TASK-574 — `productIds` field type + `ProductPickerField` + catalog search/by-ids hooks

- `frontend/features/consumer-app/types.ts`: `BlockPropType` gains `"productIds"`.
- `frontend/features/catalog/api/catalog.ts`: `catalogApi.getAll` accepts `search`/`ids` (repeated
  `ids` query params via `qs.append`).
- `frontend/features/catalog/hooks/useCatalog.ts`: `useCatalogProducts` params extended; new
  `useCatalogProductsByIds(ids)` hook (sorted-array query key, `enabled: ids.length > 0`).
- `frontend/features/consumer-app/components/BlockPropertyEditor.tsx`: `fieldSchemaFor`/
  `coerceValue`/`PropField` each gained the `"productIds"` case (schema and coercion share the
  existing `stringArray` logic per the brief); exported `FieldProps`.
- **New** `frontend/features/consumer-app/components/ProductPickerField.tsx`: debounced (300ms)
  search → `useCatalogProducts({ search })`, filtered to `isActive` and excludes already-selected
  ids (mirrors `PromoProductsSection.tsx`); ordered selected chips with thumbnail/name/price/remove;
  hides the search UI once `value.length >= def.maxItems`; empty-selection hint. Added 4 new i18n
  keys to `frontend/messages/{uk,en}.json` under `propertyEditor`.

## TASK-575 — `blockPreviews.tsx` + `AppPreviewPanel.tsx` curated-selection parity

- `blockPreviews.tsx`: `PreviewContext` gained `catalogById: Map<string, PreviewProductItem>`; new
  `resolveProductItems(props, ctx, limit)` helper (curated order via `catalogById`, silently skips
  misses, falls back to `ctx.catalog.slice(0, limit)` when `productIds` empty) used by both
  `ProductCarouselPreview` and `ProductGridPreview`.
- `AppPreviewPanel.tsx`: scans the currently-previewed page's blocks for `productGrid`/
  `productCarousel` `productIds` (`curatedProductIdsOf`, computed before the loading/error early
  returns since the new `useCatalogProductsByIds` hook must run unconditionally); new
  `toPreviewProductItem` transform (`isActive && priceRetail !== null`) shared by both `catalog` and
  the new `catalogById` map (merges the default `catalogQuery` fetch with the by-ids fetch, same
  transform, per the brief's "do not invent a second mapping").

## Verification

- `npx tsc --noEmit` (frontend): clean, both after TASK-574 and after TASK-575.
- `dotnet build` (backend, sanity check that the parallel backend-developer's TASK-571/572 changes
  this feature depends on are actually present/compiling): clean, 0 warnings/errors.
- Live browser verification (`ea@demo.local`, local dev DB, `/consumer-app/pages`, Home page,
  newly-added Product Grid block):
  - Property Editor renders "Product Ids" field with default browse list (empty query) + hint
    "With nothing selected, the first products alphabetically are shown" + "Select between 0 and 30".
  - Typed search "Молоко" → confirmed via network tab `GET /api/items?search=Молоко` (not a
    client-side filter) → returned 5 matches including items far outside the default alphabetical
    window shown by the empty-query browse list.
  - Clicked a result ("Молоко 2,5% Галичина 1л") → chip appeared in selected list, excluded from
    search results, **live preview updated instantly** to show exactly that one product/price —
    confirmed via network tab `GET /api/items?ids=<guid>` firing (the new by-ids fetch,
    `useCatalogProductsByIds` → `AppPreviewPanel`'s `catalogById`), proving the curated pick resolves
    correctly even though it's outside `/api/items`'s default `pageSize` window.
  - Removed the chip → selection empty again, hint text reappeared, live preview reverted to the
    exact same alphabetical-first-`limit` list shown before any curation (byte-identical fallback).
  - Clicked Apply → drawer closed cleanly, no console errors, block list/preview intact — confirms
    zero special-casing needed in `AppBuilderCanvas.tsx`'s existing Apply/Cancel + `onLiveChange`
    wiring (TASK-565).
  - `promotionGrid`/`promotionCarousel` untouched (not in scope, not exercised — code review
    confirms no `productIds` prop was added to their registry entries by TASK-571).

## Notes

- Browser-pane automation quirk (unrelated to this feature): the pane isn't visually composited in
  this session, so `computer.screenshot` always failed and `computer.left_click` on dnd-kit-wrapped
  "add block" cards in the Block Palette didn't register (dnd-kit's pointer sensor didn't pick up
  the synthesized events). Verification used `javascript_tool` to call `.click()` on the actual DOM
  buttons for those specific interactions (block palette "+", drawer field clicks, remove/Apply) —
  functionally identical to a user click since it invokes the same React `onClick` handlers, just a
  more reliable delivery path in this non-visual pane. Plain text-input fields (login, search box)
  were driven via `form_input`/native-setter + `input` event, same reasoning.
