# TASK-635 — Analytics: paginate the "By category" bar chart (frontend)

**Status:** done · **Agent:** frontend-developer

## Bug

`CategoryStatusChart.tsx` on `/analytics` rendered one bar per category. This tenant has 86
categories, so the chart rendered ~86 rotated X-axis labels crossing into unreadable mush.

## Fix

`frontend/features/analytics/components/CategoryStatusChart.tsx`:

- Added a `PAGE_SIZE = 20` page of bars per render (15-20 range approved; picked 20 — keeps each
  bar's label slot wide enough at this chart's usual dashboard-column width for the existing
  -20deg rotated labels to stay legible, while only needing ~5 clicks through an 86-category tenant).
- `useState` page index, clamped via `Math.min(page, totalPages)` rather than reset-via-effect, so
  a same-size background refetch doesn't yank the user back to page 1, but a filter change that
  shrinks the list still lands on a valid page.
- Sort order: **not re-sorted**. Backend (`AnalyticsRepository.GetByCategoryAsync`) already returns
  categories `OrderByDescending(critical + expired)` — worst-first — confirmed by reading the
  repository and by the live API response order. That's exactly the right "page 1" framing, so the
  chart just slices `data` as received.
- Added the shared `frontend/components/ui/Pagination.tsx` footer (prev/next + "page X/Y" + total
  count) below the chart, inside the same card. Its existing `Common.prev/next/pageOf/totalLabel`
  i18n keys already exist in both `uk.json`/`en.json` — no new translations needed. Same
  button/border/color visual language as `TablePaginationFooter` in
  `price-segments/components/TableControls.tsx`, as required.
- Kept the 4 `<Bar fill=...>` props from today's earlier Legend/Tooltip color fix untouched.
- Did not touch `analytics/page.tsx` — no prop-passing changes were needed; pagination state lives
  entirely inside `CategoryStatusChart`. The by-category `<table>` below the chart is unchanged
  (out of scope, its own separate full list).

## Verification

- `npx tsc --noEmit` — clean. `npm run lint` — clean.
- Live E2E on `/analytics` (localhost:3001 + localhost:5000, logged in as `ea@demo.local`, "All
  stores" scope → 86 categories from `GET /api/analytics/by-category`).
  - Browser-pane screenshot compositing was unavailable this session (`document.hidden: true`,
    tab backgrounded — same environment limitation TASK-634 hit on this exact chart earlier
    today), and Recharts' `ResponsiveContainer` measured 0 width under it (no `ResizeObserver`
    firing on a hidden document), so axis/bar SVG geometry didn't render. Verified via DOM/React
    fiber prop inspection instead:
    - Pagination footer rendered `Total: 86`, `1 / 5`.
    - `BarChart`'s `data` prop had exactly 20 items on page 1; clicking Next moved to page 2 whose
      first item (`Творожная масса эколин`) is exactly category #21 in the API response, proving
      the slice math is correct.
    - Clicked to the last page: `5 / 5`, Next button disabled, `BarChart` data had exactly 6 items
      (86 − 80 remainder) — matches the brief's "last page shows the remainder" requirement.
    - Legend swatches still resolved to the 4 distinct fills from today's earlier fix
      (`#4ADE80`/`#FBBF24`/`#F87171`/`#DC2626`), confirming that change wasn't disturbed.

## Files touched

`frontend/features/analytics/components/CategoryStatusChart.tsx` only.
