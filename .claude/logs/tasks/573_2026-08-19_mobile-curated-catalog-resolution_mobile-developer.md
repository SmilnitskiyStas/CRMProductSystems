# TASK-573 — Mobile: curated-selection resolution

**Status:** done
**Agent:** mobile-developer

Full brief: TASK-570 log (`.claude/logs/tasks/570_2026-08-19_catalog-curation-architecture_project-architect.md`),
ADR-032.

## What changed

- `mobile/features/consumer-content/api.ts` — `getConsumerCatalogByIds(context, ids)`. Verified
  axios's default paramsSerializer (`node_modules/axios/lib/helpers/toFormData.js`, default
  `indexes: false`) emits `ids[]=<guid>`, not the `ids=<guid1>&ids=<guid2>` shape ASP.NET Core's
  `[FromQuery(Name="ids")] Guid[]` binder needs — no existing multi-value query param precedent
  elsewhere in `mobile/`, so this call builds the query string by hand.
- `mobile/features/consumer-content/hooks.ts` — `useConsumerCatalogByIds(context, ids)`, mirrors
  `useConsumerCatalog`, `enabled: Boolean(context) && ids.length > 0`.
- `mobile/features/server-driven-ui/PageRenderer.tsx` — `page` lookup moved before the data hooks
  (still ahead of the `if (!page) return null` early return, hook order unaffected); new
  `getCuratedProductIds` helper unions `productIds` across the page's `productGrid`/
  `productCarousel` blocks; `catalogByIds` query added; `catalogById` Map merges the existing
  page=1/pageSize=30 fetch with the by-ids result and is passed into `resolvePage` alongside the
  unchanged `catalog` array.
- `mobile/features/server-driven-ui/resolveBlocks.ts` — `BlockDataSources.catalogById` added;
  `productCarousel`/`productGrid` case rewritten per ADR-032's algorithm: non-empty `productIds`
  resolves in admin order via `catalogById`, silently skipping misses and null-price items, capped
  to `limit`; empty/absent `productIds` keeps the exact prior `data.catalog.filter(...).slice(...)`
  fallback. `title`/`showViewAll`/`columns`/`cardWidthPx` passthrough untouched (no regression on
  TASK-562/569).
- `mobile/features/server-driven-ui/__tests__/resolveBlocks.test.ts` — added a `catalogById` field
  to the shared fixture, and a new `curated productIds selection` describe block: (a) admin-order
  resolution, (b) missing id skipped, (c) null-`priceRetail` id skipped, (d) cap at `limit`, (e)
  empty array vs. absent prop are identical to each other (regression guard on the fallback branch).
- `blocks/types.ts`, `blocks/validators.ts`, `blocks/CoreBlocks.tsx` — untouched, confirmed via
  `git status` (only the 5 files above changed): `productIds` is consumed and stripped during
  resolution exactly like `limit`, never reaching `ProductCollectionProps`.

## Verification

- `npx jest features/server-driven-ui/__tests__/resolveBlocks.test.ts` — 30/30 pass.
- `npx jest` (full mobile suite) — 54 suites / 269 tests pass, no regressions.
- `npx tsc --noEmit` — clean.
- Diff reviewed: a block with no `productIds` in `props` resolves through the unchanged `else`
  branch — byte-identical to pre-task behavior.

Real end-to-end verification (web preview / real device against a live backend) still needs
TASK-572's endpoint actually deployed — out of scope for this task per the brief.
