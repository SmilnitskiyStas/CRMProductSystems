# TASK-583 — Remove local store pickers on Orders and AI Orders pages

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-20 · **Next:** none

## Summary

Both pages had their own local store `<select>`, redundant with the global header
`StoreSelector` (`frontend/lib/useStoreContext.ts`). Removed both, wired the pages onto
`usePrimaryStoreId()` instead. No backend changes — `POST /api/orders/calculate` and
`POST /api/ai-orders/generate` already require a single concrete `Guid StoreId`, which is
exactly what `usePrimaryStoreId()` produces (`undefined` when the header is in "all stores"
mode).

## Orders page — `frontend/app/(dashboard)/orders/page.tsx`

- Removed `storeId`/`setStoreId` local state, `useStores()`, the `<select>` block, and the
  now-dead `selectStyle` const and `effectiveStoreId` fallback chain.
- Added `usePrimaryStoreId()`; it's used directly as the mutation's store id.
- "Generate" button: `disabled={generate.isPending || !primaryStoreId}`. When no primary store
  is selected, a hint (`t("selectStoreHint")`) renders next to the button.

## AI Orders page — `frontend/app/(dashboard)/ai-orders/page.tsx`

- Removed `storeId`/`setStoreId`, `useStores()`, the `<select>` block (incl. its
  `t("allStores")` option — that key was scoped to this page's namespace only, confirmed via
  grep no other file references `Dashboard.aiOrders.page.allStores`, so it was deleted from
  both message files), and `selectStyle`/`effectiveStoreId`.
- List (`useAiOrders(primaryStoreId)`): hook signature was already `storeId?: string`, so
  passing `primaryStoreId` (`string | undefined`) works unchanged — "all stores" (undefined)
  still shows unfiltered history, unchanged behavior, not gated.
- Generate button: same disable/hint pattern as Orders.
- Added a small `useEffect` resetting `selectedId` (review panel) whenever `primaryStoreId`
  changes, to preserve the old picker's UX of clearing the selected review when the store
  context changes (previously done inline in the picker's `onChange`).

## i18n

Added `selectStoreHint` to both `Dashboard.orders.page` and `Dashboard.aiOrders.page`
namespaces, in both locale files (project has exactly two: `en.json`, `uk.json`):

- en: "Select a specific store at the top of the page to generate an order"
- uk: "Оберіть конкретний магазин угорі сторінки, щоб сформувати/згенерувати замовлення"
  (verb matches each page's own "generate" wording)

Removed `Dashboard.aiOrders.page.allStores` key (page-scoped, unused elsewhere after the
`<select>` removal). Left the several *other* `allStores`/`allStoresOption` keys in other
pages' namespaces untouched (analytics/pos, sales, notifications filter drawer — unrelated to
this task).

## Verification

- `npx tsc --noEmit` in `/frontend`: clean.
- `npx eslint` on both changed page files: clean.
- Both `messages/en.json` and `messages/uk.json` validated as parseable JSON.
- Live browser check: started `backend-dev` + a throwaway `frontend-dev` (port 3001 from
  `.claude/launch.json` is Windows-reserved on this machine, ran on 3057 instead, not
  committed to launch.json) and opened the landing page. No authenticated session was
  available in the browser pane (empty localStorage, no token) and logging in was correctly
  out of scope for this agent — did not attempt it. Dashboard-level manual check (both pages,
  both single-store and all-stores header modes) was **not** performed; left for the
  orchestrator/user to do against an authenticated session. Both throwaway dev servers were
  stopped afterward, no lingering processes.

## Files changed

- `frontend/app/(dashboard)/orders/page.tsx`
- `frontend/app/(dashboard)/ai-orders/page.tsx`
- `frontend/messages/en.json`
- `frontend/messages/uk.json`
