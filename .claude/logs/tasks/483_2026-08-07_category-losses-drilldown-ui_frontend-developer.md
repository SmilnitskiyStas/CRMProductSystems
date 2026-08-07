# TASK-483 — Frontend: category/losses product drill-down UI (interactive analytics + margin plan)

**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-481 (backend, done) ·
**Next:** TASK-484 (frontend-developer, POS product-trend UI, separate scope — not touched here),
TASK-486 (qa-tester, live E2E for this together with TASK-484/485)

Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md`

## What was built

Consumed TASK-481's two endpoints end-to-end: types → api → hooks → 2 new panel components →
3 chart components rewired for click → `roles.ts` → page wiring → i18n. Built on top of
TASK-485's already-merged `ExpiryDonut`/`PosDayDetailPanel` changes without touching them.

- `frontend/features/analytics/types.ts` — `CategoryProductBreakdownDto`/`CategoryProductRowDto`,
  `LossesByProductDto`/`LossByProductRowDto`, verified field-for-field against the live
  `AnalyticsDtos.cs` records and controller actions, not the plan doc's paraphrase.
- `frontend/features/analytics/api/analytics.ts` — `getCategoryProductBreakdown`,
  `getLossesByProduct`. `category_id: null` (uncategorized) is never put on the wire — same
  "omitted = uncategorized" convention the backend's `Guid? category_id` already uses.
- `frontend/features/analytics/hooks/useAnalytics.ts` — `useCategoryProductBreakdown`,
  `useLossesByProduct`. Full filter object in the query key, no `placeholderData`/
  `keepPreviousData`, matching `useMarketingAnalyticsOverview`'s documented discipline.
- `frontend/features/analytics/components/CategoryDetailPanel.tsx` (new) — sortable/paginated
  (client-side; the endpoint returns one category's full product list, no server pagination in
  its contract) table via the shared `SortableHeader`/`TablePaginationFooter`
  (`marketing-analytics/price-segments/components/TableControls.tsx`). Margin columns
  (header cells AND body cells) only enter the JSX tree at all when `canViewAnalyticsMargin(...)`
  is true — conditional render, not a CSS hide. Visible margin disclaimer line ("«Оцінна маржа»
  розрахована за поточною закупівельною ціною…") plus "(оцінна)" in both margin column headers —
  satisfies ADR-027's binding UI requirement without repeating the caveat per cell.
- `frontend/features/analytics/components/LossesProductBreakdownPanel.tsx` (new) — shared by
  both the losses-by-store and losses-by-reason drill-downs, `{ title, totalLoss, storeId?,
  reason?, from, to, onClose }` exactly as specified. No margin columns (DTO has none).
- `CategoryStatusChart.tsx` / `LossesByReasonChart.tsx` / `LossesByStoreChart.tsx` — added
  click props, using the verified-working recharts 3.8.1 `<Bar onClick={(entry) =>
  ...entry.payload.X}>` mechanism from `SegmentDistributionChart.tsx` (copied the actual working
  pattern, not the plan's recharts@2-shaped snippet — same caution TASK-485 already flagged for
  this codebase's installed recharts version).
- `frontend/lib/roles.ts` — `canViewAnalyticsMargin`, exact shape of
  `canExportMarketingAnalyticsPii` with `AT_LEAST_NETWORK_MANAGER` + `"analytics.view_margin"`.
- `frontend/app/(dashboard)/analytics/page.tsx` — new selection state, rewired the by-category /
  by-reason / by-store row `onClick`s from `router.push` to toggle handlers, wired chart click
  props, renders both new panels conditionally. Read fresh before editing — built on top of
  TASK-485's `ExpiryDonut`/state additions, didn't revert anything.
- `frontend/messages/uk.json` / `en.json` — new keys under `Dashboard.analytics.categoryDetailPanel`,
  `Dashboard.analytics.lossesProductBreakdownPanel`, `Dashboard.analytics.page.lossesProductPanelTitle`.

## Noted deviations from the brief (judgment calls, CLAUDE.md-sanctioned)

1. **`selectedCategoryId` is `string | null | undefined`, not the brief's literal `string |
   null`.** A category id is itself nullable (null = "uncategorized" bucket, same convention the
   existing by-category table already uses for its row key). A plain two-state type can't
   distinguish "nothing selected" from "uncategorized selected" — both would collapse onto the
   same `null`. `undefined` = no panel open, `null` = uncategorized panel open, a string = that
   category's panel open. `CategoryStatusChart`'s `selectedCategoryId` prop carries the same
   3-state type for the same reason. No sentinel strings anywhere — the domain's own `string |
   null` convention is preserved throughout, just with an explicit third "unset" state layered
   on top the way `useState`'s natural initial value already implies.
2. **`CategoryStatusChart`'s active/inactive treatment is opacity, not the two-tone color swap**
   `SegmentDistributionChart.tsx` uses. That chart is single-series; this one is a 4-series
   stacked bar where color is already load-bearing (safe/warning/critical/expired). Swapping to
   ACTIVE/INACTIVE colors would destroy that coding, so selection is expressed as `fillOpacity`
   (1.0 selected / 0.3 dimmed / 1.0 for all when nothing's selected) on top of each segment's own
   real status color instead.
3. Client-side sort + pagination in both new panels (not server-side) — neither new endpoint
   accepts `page`/`pageSize`/`sortBy` (confirmed against the live controller actions), and both
   return one bounded response (one category's products / one store-or-reason's products), so
   `SortableHeader`/`TablePaginationFooter` are reused for their UI/interaction contract only.

## Verification

- `npx tsc --noEmit` — clean.
- `npm run lint` — clean, no warnings.
- `npm run build` — exit 0, all 57 static pages generated; `/analytics` route present
  (8.87 kB / 247 kB First Load JS). `/analytics/pos` unchanged (not in this task's scope).
- Live browser E2E **not done** — no reachable dev-stack wired to this working tree's
  uncommitted changes in this session (only unrelated pre-built staging containers were up,
  same constraint TASK-485 hit); deferred to TASK-486 per the plan, same as TASK-485/484.

## Explicitly out of scope (per brief)

`PosTopProductsTable.tsx`, `PosRevenueTrendChart.tsx`, `ProductAnalyticsTab.tsx`,
`analytics/pos/page.tsx`, `api/pos-analytics.ts`, `hooks/usePosAnalytics.ts` — untouched, that's
TASK-484. New panels show current-period data only (no `compareFrom`/`compareTo` ever passed to
either new hook) — satisfies the plan's "snapshot, not trend" rule for QA to verify.
