# TASK-587 — Remove local store picker on Sales page

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-21 · **Next:** none

## Summary

Sales page (manual daily-sales entry) had three local store pickers, all reading
`useStores()` independently of the header's global `StoreSelector`. Removed all three,
wired the page onto `usePrimaryStoreId()` (`frontend/lib/useStoreContext.ts`) — same
pattern as TASK-583 (Orders, AI Orders). No backend changes: `GET /api/daily-sales`
already accepts an optional `store_id` (unfiltered = all stores), while
`POST /api/daily-sales` and `POST /api/daily-sales/import` both require a single
concrete `Guid StoreId` — exactly what `usePrimaryStoreId()` yields.

## `frontend/app/(dashboard)/sales/page.tsx`

- Removed `storeId`/`setStoreId` state, `useStores()`, the filter-row `<select>`
  (with its `t("allStores")` option), and the `stores[0]?.id` fallback (`defaultStoreId`).
- Added `usePrimaryStoreId()`; passed directly into `useDailySales({ storeId: primaryStoreId, from, to })`
  — list is *not* gated, shows unfiltered results in "all stores" mode (unchanged from before).
- "Add Sales" and "Import CSV" buttons: `disabled={!primaryStoreId}`, with **one shared**
  `t("selectStoreHint")` shown once next to both (not duplicated per-button — both are
  disabled for the same reason).
- `SaleEntryForm`/`CsvImportDialog` now only render when `entryOpen/importOpen && primaryStoreId`
  and receive `primaryStoreId` as a plain `storeId` prop (Modal is a full-screen overlay, so
  the header selector can't change underneath an open modal).
- `handleImport` moved the `storeId` from the callback's argument to the page's own
  `primaryStoreId` closure (with a defensive `if (!primaryStoreId) return`).

## `frontend/features/sales/components/SaleEntryForm.tsx`

- Replaced `stores: Store[]` / `defaultStoreId: string` props with a single fixed `storeId: string`.
- Removed the RHF-registered `storeId` select field and its zod validator entirely — store is
  no longer user-editable inside the form; `submit()` reads `storeId` from the prop closure.
- Removed now-unused `StoreDto` import.

## `frontend/features/sales/components/CsvImportDialog.tsx`

- Removed local `useState(storeId)` and its `<select>`; `onImport` signature simplified from
  `(storeId, file)` to `(file)` — store comes from the page's `primaryStoreId` now.

## i18n (`frontend/messages/en.json`, `frontend/messages/uk.json`)

Both locale files, `Dashboard.sales.page` namespace:
- Added `selectStoreHint` — en: "Select a specific store at the top of the page to add or
  import sales"; uk: "Оберіть конкретний магазин угорі сторінки, щоб додати продажі або
  імпортувати CSV" (one shared string, covers both gated actions).
- Removed `allStores` (was only used by the deleted `<select>`, confirmed via grep no other
  page/component reads `Dashboard.sales.page.allStores` — distinct namespace from
  `analytics/pos`'s and `StoreSelector`'s own `allStores` keys, which are untouched).

`Dashboard.sales.entryForm` and `Dashboard.sales.csvImport` namespaces: removed both now-dead
`storeLabel` keys and `entryForm.validation.selectStore` (confirmed via grep no longer
referenced anywhere).

## Verification

- `npx tsc --noEmit` in `/frontend`: clean.
- `npx eslint` on all 5 touched files: clean.
- Both `messages/en.json` and `messages/uk.json` validated as parseable JSON.
- Started `backend-dev` (port 5000) + `frontend-dev` (port 3001) via `.claude/launch.json`:
  both booted with no server errors. No authenticated browser session existed (empty
  localStorage/sessionStorage) — logging in was correctly out of scope, so the
  authenticated-mode check (list load, disabled/hint behavior, end-to-end sale add) was
  **not** performed; left for the user/orchestrator to spot-check. Both dev servers stopped
  afterward.

## Files changed

- `frontend/app/(dashboard)/sales/page.tsx`
- `frontend/features/sales/components/SaleEntryForm.tsx`
- `frontend/features/sales/components/CsvImportDialog.tsx`
- `frontend/messages/en.json`
- `frontend/messages/uk.json`
