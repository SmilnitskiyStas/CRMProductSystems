# TASK-430: AudienceBuilder frontend (Фаза 3)

**Agent:** frontend-developer
**Date:** 2026-07-27
**Status:** done — `tsc --noEmit`/`npm run build` clean, live-verified end-to-end in browser
against real seeded data, no blocker.

## Context

Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фази 2-4" (Фаза 3 roadmap item).
Design doc: scratchpad `phase3-audience-builder-design.md` §10 (frontend structure). Backend
contract: `.claude/logs/tasks/429_2026-07-27_audience-builder-backend_backend-developer.md` — used
as the sole source of truth per the brief (cross-checked directly against the actual
`AudienceBuilderController.cs`/`AudienceBuilderDtos.cs`/`AudienceBuilderSortKeys.cs` — the task
log's contract matched the code exactly). Competitive analysis:
`docs/uployal/AUDIENCE_PREPARATION_ANALYSIS.md`. Reference implementation (Фаза 2):
`frontend/features/marketing-analytics/price-segments/`.

## Done

New `frontend/features/marketing-analytics/audience-builder/` + one page route.

### Files
- `types.ts` — all DTOs/requests transcribed from the verified backend contract, plus
  frontend-local `AudienceTerm`/`AudienceFilterState`/`CompetitorFilterState`/`TablePagingState`.
- `api/audienceBuilder.ts` — `audienceBuilderApi` (10 endpoints) + `toTermRequests`/
  `buildAudienceRequest`/`buildExportAudienceRequest`/`buildCompetitorRequest`/
  `buildExportCompetitorRequest` builder functions.
- `store/useAudienceBuilderStore.ts` — Zustand, UI/session state only (terms, mode, thresholds,
  excludedItemIds, isBuilt, competitorTerms, horizon). Not persisted.
- `hooks/useAudienceBuilder.ts` — React Query wrappers, query key = full request object per the
  brief, no `placeholderData`.
- `components/`: `TermSearchBar.tsx`, `TermChips.tsx`, `CombineModeToggle.tsx`,
  `ThresholdInputs.tsx`, `BuildResetBar.tsx`, `ResultTabs.tsx`, `AudiencePeriodBar.tsx` (see
  deviation below), `BuyersTab/{BuyersKpiCards,BuyersTable,ExportReceiptsButton}.tsx`,
  `CompetitorTab/{CompetitorTermInput,HorizonToggle,CompetitorKpiCards,CompetitorBuyersTable}.tsx`,
  `MatchedItemsTab/{MatchedItemsTable,BulkSelectionActions}.tsx`.
- `frontend/app/(dashboard)/marketing-analytics/audience-builder/page.tsx`.
- `frontend/components/layout/Sidebar.tsx` — third item in the `marketing_analytics` group
  (`Target` icon).
- `frontend/messages/{en,uk}.json` — new `Dashboard.audienceBuilder.*` namespace (page, periodBar,
  termSearchBar, termChips, combineModeToggle, thresholdInputs, buildResetBar, resultTabs,
  buyersKpi, buyersTable, exportReceipts, competitorTermInput, horizonToggle, competitorKpi,
  competitorTable, matchedItemsTable, bulkSelectionActions) + sidebar label. Reuses
  `Dashboard.priceSegments.table` (Back/Next) unchanged via the shared `TablePaginationFooter`.

Reused directly, not duplicated: `SortableHeader`/`TablePaginationFooter` from
`price-segments/components/TableControls.tsx` (all 3 tables), `canExportMarketingAnalyticsPii`/
`hasRole`/`CAN_VIEW_ANALYTICS` from `lib/roles.ts`, `downloadFilePost` from `lib/download.ts`,
`useStores` from `features/stores/hooks/useStores.ts`, `useRequireTab`, `AccessDenied`.

## Architecture decisions (judgment calls, documented per CLAUDE.md's gate)

1. **Container/presentational split matches price-segments/page.tsx exactly**: `page.tsx` owns
   every React Query hook call and all pagination/sort/active-tab state; table components are
   presentational (`data`/`isLoading`/paging props in, no query calls). Terms/mode/thresholds/
   exclusions/competitor state live in the Zustand store instead of page-local `useState` —
   the one deliberate structural difference from Фаза 1/2, justified because leaf control
   components (TermChips, ThresholdInputs, MatchedItemsTable's checkboxes, CompetitorTermInput,
   HorizonToggle) sit 2-3 folders deep and would otherwise need the setters prop-drilled through
   `ResultTabs`/`BuyersTab`/`CompetitorTab`/`MatchedItemsTab`. Period (`from`/`to`) + store
   selection stay page-local `useState`, NOT in the store — consistent with Фаза 1/2's own
   convention, and the store's `reset()` deliberately does not touch them (see below).
2. **`AudiencePeriodBar.tsx` is a new, self-contained component, not a reuse of
   `price-segments/ComparisonFilterBar.tsx`.** Two reasons: (a) `AudienceBuildRequest` takes raw
   `From`/`To` with no server-resolved period-preset concept (confirmed in the actual DTO — unlike
   PriceSegments' `period=30|60|90|custom`), so a plain two-date-input control is the correct shape,
   not a preset selector; (b) this repo's own precedent is that every phase gets its OWN period/
   store filter component (Фаза 1's `PeriodStoreFilterBar` vs Фаза 2's own `ComparisonFilterBar`,
   neither reusing the other) — price-segments/types.ts's own header comment documents this
   "self-contained/portable" convention explicitly. `ComparisonFilterBar` also carries a
   `hidePeriod` prop and 30/60/90 preset shape this feature has no use for.
3. **Matched-items exclusion refetches ALL THREE own-audience queries (overview/buyers/
   matched-items) together, uniformly, via the shared full-filter-object query key** — a
   deliberate deviation from the design doc's aspirational "toggling a checkbox does NOT refetch
   the matched-items table, only debounce KPI/buyers ~300ms" note. Two reasons: (a) the verified
   backend contract's own description says "toggling a checkbox and re-calling this endpoint
   (`/matched-items`) is how the UI refreshes it" — i.e. the backend agent's own contract already
   assumes a refetch happens; (b) the mandatory brief requirement is "миттєвий перерахунок"
   (INSTANT recalculation) — a debounce works against that, not for it. No debounce added. One
   real optimization kept from the spirit of the design doc's note: `matchedItemsPage` resets to 1
   only when the *search* filter changes (from/to/storeIds/terms/mode/minQuantity/minAmount) — NOT
   when only `excludedItemIds` changes — since the matched-item *set* never changes on exclusion,
   only each row's `isExcluded` flag, so there's no reason to bounce the reader back to page 1.
   Buyers' own page-reset effect uses the full filter (population genuinely changes).
4. **Overview is fetched whenever `isBuilt`, independent of which result tab is active** (unlike
   the buyers/matched-items/competitor-buyers table queries, each gated additionally on their own
   tab being visible) — it's a cheap single-aggregate-row query with TWO consumers on two
   different tabs (`BuyersKpiCards` and `MatchedItemsTable`'s "Обрано X з Y", which reuses
   `overview.itemsInSelectionCount` as the numerator — the exact same server-computed count the
   KPI card shows, rather than an approximate client-side `total − excludedItemIds.length`).
5. **Export buttons own their own mutation + PII-unmask toggle internally** (receiving only the
   already-assembled filter state as a prop), mirroring price-segments' `ExportPriceAudienceButton`
   pattern exactly — not routed through page.tsx. The competitor tab's export has no dedicated file
   (unlike the Buyers tab's explicit `ExportReceiptsButton.tsx`) since the brief's file list didn't
   name one for it; it lives inline in `CompetitorBuyersTable.tsx`.
6. **`store.reset()` does not touch period/store selection** — only the term-builder state
   (terms/mode/thresholds/exclusions/`isBuilt`/competitor state). "Скинути" clears the query the
   user was building, not the surrounding date-range/store context they were already working in.
   `page.tsx`'s `handleAfterReset` callback additionally clears page-local UI state the store
   doesn't own (active result tab, all three tables' pagination).
7. **PII on-screen phone rendering is NOT re-masked client-side** — confirmed via the backend
   contract that `buyers`/`competitor/buyers` reads already resolve `CanViewUnmaskedPii`
   server-side from the caller's own role (unlike price-segments, where on-screen phone is never
   masked at all and only exports are). `BuyersTable`/`CompetitorBuyersTable` render `row.phone`
   exactly as received.

## Live browser verification (dev stack, real Postgres data — not a mock)

Started `backend-dev`(:5000)/`frontend-dev`(:3000) via `preview_start`, logged in as the seeded
`ea@demo.local` (enterprise_admin, tenant "Свіжий Кут"). Confirmed via direct `psql` that this
tenant has 125 customer-linked POS transactions across 6 real products (Молоко, Кефір, Вода,
Сметана, Хліб, Куряче філе, Огірки) — real test data, not synthetic seeding done for this task.

Verified end-to-end, matching every item in the brief's mandatory checklist:
1. Empty state → "Сформувати список" correctly disabled with 0 terms.
2. Text term "Молоко" → Enter → chip appears, placeholder flips to "ще товар…".
3. Second term "Кефір" → `CombineModeToggle` appears (was absent with 1 term).
4. OR (`Будь-який товар`) → 11 participants / 38 units / 1170 ₴; AND (`Усі товари`) → 0 (no
   customer bought both, in this real dataset) — genuinely different numbers, and the 0-result
   state renders cleanly (`Немає покупців...`) with no crash.
5. Min-quantity threshold (5) → narrows 11 → 4, status line shows "2 умов · будь-який · від 5 шт",
   pagination footer correctly collapses to a plain count when everything fits on one page.
6. "Знайдені товари" tab: both matched items shown, including "Кефір 1% Простоквашино 1л" with
   **0 sold / 0 receipts / 0 buyers** — the zero-sales-still-shown requirement, live, not assumed.
7. **Core requirement**: unchecked "Молоко" (11/11 buyers) → status line instantly shows
   "вилучено 1 товар(ів)", "Обрано 1 з 2" → switched to "Покупці товару" tab → KPIs and buyers
   table both instantly show 0/1/0/0₴ and the correct empty state, together, no stale/mismatched
   numbers. "Обрати всі" correctly restored full selection.
8. Competitor tab: empty-state hint before any competitor term; added "Вода" → KPIs appear
   (1 new audience / 1 item / 2 units / 28₴) with a real named customer row; horizon toggle
   changes the hint text correctly. **Toggling InPeriod↔AllTime**: confirmed via
   `read_network_requests` that the two calls produced DIFFERENT `filtersHash` values
   (`367d5a2aad2575c3` vs `693a7c678ff78293`) — proof the wire payload genuinely differed — even
   though the resulting count coincidentally stayed at 1 for this specific real dataset (verified
   by hand-querying the purchase history: no customer in this tenant happens to have a
   pre-window-only purchase of the own items among this competitor's period buyers). Backend's own
   dedicated regression test (task log 429) already covers the size-differs case with controlled
   data — not re-litigated here.
9. Real server pagination: "Вперед" on page 1/2 → page 2 shows the 11th customer (not shown on
   page 1), KPIs unchanged — confirmed server-side, not a client slice.
10. "Скинути" → full return to the initial empty state (chips, mode toggle, tabs, status line all
    cleared).
11. Export button text: "Вивантажити чеки (XLSX)" + caption "Файл на рівні чеків: один рядок = один
    чек, не один покупець." — confirmed visible without needing to actually trigger a download.
12. Zero console errors/warnings across the entire session (`read_console_messages`).

**Data gap noted, not a code defect**: `categories` table is empty (0 rows) across the ENTIRE dev
DB (`SELECT count(*) FROM categories` → 0), so the category-typeahead path could only be verified
structurally (correct request fired, correct empty response, correct "Категорій не знайдено"
render) rather than with a populated suggestion list. Flagging for whichever task eventually seeds
category data, so this path gets a full live pass with real suggestions/item-counts.

Stopped both preview servers after verification.

## Verification

- `npx tsc --noEmit` — 0 errors.
- `npm run build` — exit 0, all 55 routes generated, `/marketing-analytics/audience-builder` listed
  (11.2 kB / 135 kB First Load JS). Build output contains repeated `ENVIRONMENT_FALLBACK` stderr
  noise during static generation — confirmed pre-existing/unrelated: it appears for dozens of
  routes this task never touched (`/admin`, `/ai-orders`, `/analytics`, etc.), exit code is 0, and
  no `Failed to compile`/`Type error`/`Module not found` anywhere in the output.
- Live browser verification — see above.

## Not in scope (per brief)

- Backend/mobile untouched.
- Existing RFM/PriceSegments UI untouched (read only, as the architectural sample).
- "Saved named audiences" — out of scope per design doc §11 (confirmed already not built
  backend-side either).

## Git

Not committed (repo convention — main session/user commits).
