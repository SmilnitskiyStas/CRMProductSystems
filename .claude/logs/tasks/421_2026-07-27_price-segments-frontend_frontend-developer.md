# TASK-421: Frontend — Фаза 2 price segments + frequency/reactivation

**Agent:** frontend-developer
**Date:** 2026-07-27
**Status:** done — builds clean, live-verified end-to-end in browser, no blocker. Continuation of
a session that hit its usage limit mid-task (not a code error) — this pass verified the prior
agent's partial work first, then completed the rest.

## Context

Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фази 2-4". Design doc: scratchpad
`phase2-price-segments-design.md` §6. Backend contract: task log 420 (source of truth over the
scratchpad doc for exact field names). Competitive source:
`docs/uployal/PRICE_SEGMENTS_ANALYSIS.md`.

## Verified from the interrupted prior agent (1 bug fixed, nothing rewritten)

Read all 11 pre-existing files in full: `types.ts`, `api/priceSegments.ts`,
`hooks/usePriceSegments.ts`, `ModeTabs`/`ComparisonFilterBar`/`PriceSegmentChart`/
`PriceAudienceCards`/`PriceAudienceTable`/`ExportButtons.tsx` + bonus `RecommendationBlock.tsx`/
`TableControls.tsx`. All matched the backend contract's field names precisely
(`analyzedCount`/`currentPeriodBuyerCount`/`previousPeriodBuyerCount` kept distinct throughout) and
`PriceAudienceTable` genuinely uses server pagination (page/pageSize/sortBy/sortDescending query
params, `totalPages` from the DTO — no client-side slicing).

**One real bug, caught by `tsc --noEmit` before writing anything new**: `api/priceSegments.ts`'s
`buildStoreQs` typed its `extra` param as `Record<string, string | number | undefined>` (missing
`boolean`), so passing `sortDescending` at the two all-time call sites failed to compile. Fixed by
widening the type to match `buildPeriodQs`'s (1-line fix).

**ExportButtons.tsx inline-style question (flagged in the brief) — resolved, not fixed**: compared
directly against Фаза 1's RFM sibling components (`PeriodStoreFilterBar.tsx`, `SegmentGrid.tsx`,
`RecommendationCard.tsx`, and `components/ui/Btn.tsx` itself). The inline-`style`-with-hex-colors
approach used throughout ALL of `price-segments/` is a byte-for-byte match of the convention
already established in `features/marketing-analytics/` — this feature area doesn't use
Tailwind/shadcn classes anywhere, RFM included (`Btn.tsx` itself is inline-styled). Left as-is per
the brief's own "if it's consistent with something existing, leave it" instruction.

## What this pass built

- `app/(dashboard)/marketing-analytics/price-segments/page.tsx` — 3 modes (comparison/frequency/
  all-time) as tabs on one route, mirrors RFM's `page.tsx` gating pattern (`useMe`/`hasRole`/
  `useRequireTab`/`useModules`, no new module key). Comparison+Frequency share one period+store
  filter object (atomic re-key on any change — no `keepPreviousData` anywhere, same as Фаза 1);
  All-time reuses the same filter bar with `hidePeriod`. All-time's own overview query is also
  kept warm while Frequency is open, purely to source real ₴ tier-range labels for Frequency's
  price-segment filter dropdown (same live network-wide boundaries in both modes per design doc
  §1.2 — avoids a second boundary-fetching endpoint just for filter labels).
- `components/AllTimeView/`: `AllTimeKpiCards` (4 KPIs + nullable insight chips with an explicit
  "not enough history yet" state), `MedianCheckTrendChart` (recharts dual-Y-axis line chart —
  median check + items/receipt, empty-state for brand-new tenants), `SegmentDistributionChart`
  (clickable bars AND a duplicate button row below them — analysis doc §13 documents the
  competitor offering both entry points), `SegmentRecommendationCard` (wraps
  `RecommendationBlock`; renders the competitor's own "Оберіть сегмент нижче" hint when
  `recommendation` is null, never an error state), `AllTimeCustomerTable` (server-paginated,
  reuses `TableControls`/`ExportButtons`, deliberately no recommendation slot — that DTO genuinely
  carries none).
- `components/FrequencyView/`: `FrequencyKpiCards` (both at-risk denominators shown side by side,
  never just one), `FrequencyModeFilter` (4 audience cards + decline-threshold input shown ONLY
  for Declining + min/max spend + price-segment select, with a Sleeping-specific label swap to
  previous-period wording — fields stay enabled, per the analysis doc's own recommended fix vs the
  competitor's broken equivalent), `FrequencyAudienceTable` (server-paginated, renders "—" for
  null `typicalCheckCurrent`/`frequencyDeltaPercent`, never "0"/"∞").
- Sidebar: second item in the existing `marketing_analytics` NavGroup
  (`/marketing-analytics/price-segments`, reuses `CAN_VIEW_ANALYTICS` + the already-imported
  `TrendingUp` icon, no new module key).
- i18n: full `Dashboard.priceSegments.*` namespace added to both `en.json`/`uk.json` — confirmed
  via grep that the prior agent's 8 components already called
  `useTranslations("Dashboard.priceSegments.*")` but NEITHER message file had any key under that
  namespace yet. Grepped every existing `t("...")` call site across all components first to get
  the exact required key set, then added this pass's own new keys for the page/allTime/frequency/
  `*Table` namespaces. Both files validated with `JSON.parse` after editing.

## Build/test status

`npx tsc --noEmit` — clean (0 errors) after the 1-line fix above.
`npm run build` — exit 0, `/marketing-analytics/price-segments` route present (16.2 kB, 252 kB
First Load JS, in line with sibling analytics routes).

## Live browser verification (dev stack, real Postgres + backend, not mocked)

Found and fixed an unrelated environment issue first: a stray `node.exe` orphaned from a previous
session was holding port 3000, so `frontend-dev` auto-bumped to port 58083 on first start — the
backend's CORS policy only allows `localhost:3000` (`Program.cs`'s `Cors:Origins` default), so
every authenticated API call failed client-side with `net::ERR_FAILED` despite the OPTIONS
preflight returning 204. Killed the orphaned process, restarted `frontend-dev` on port 3000,
confirmed CORS/data flow fixed before continuing.

Logged in as the seeded `store_manager` demo user against the existing dev tenant's real
customer-linked POS data (11-13 seeded customers with real names such as "AtRisk One"/"Champion
One"/"Hibernating One" left over from Фаза 1's own RFM verification). The Browser pane's visual
tools (screenshot/read_page) were non-functional in this session ("pane is not displayed"), so
interactions were driven via `javascript_tool` (finding buttons by text and dispatching
`.click()`) with `get_page_text` reading back the real rendered DOM after each step — not a
simulation, the actual React/Next.js app responding to real clicks and real API calls. Verified in
order:

- All 3 tabs render; Comparison mode's 5 KPIs + distribution chart + 4 audience cards.
- Switching 30→60 days atomically recalculated every number (`analyzedCount` 1→2, raised 0→1,
  price index +1.9%→-0.3%) and the already-open "Стабільні" audience table refetched to a
  DIFFERENT real customer for the new period — no stale-data mixing.
- "Проаналізовано" (1) vs current-period buyers (9) vs previous-period buyers (3) rendered as
  three visibly distinct numbers with an explanatory footnote — never conflated.
- Clicked a real audience card → real server-paginated table (customer name/phone/segment
  transition/items-per-receipt/typical check/LTV) + a live Тригер/Дія/Оффер/Застереження
  recommendation citing the real live count/LTV + "Пояснити детальніше" button, all present.
- All-time mode: KPIs, dual-axis monthly trend chart, distribution chart with the "Оберіть
  ціновий сегмент нижче…" hint before any selection; real 2-page server pagination on the
  13-customer base. Clicked the "83–96 ₴" tier button → table filtered to exactly the 3 real
  customers in that tier AND the recommendation populated with that tier's live count/LTV, both
  driven by the same click.
- Frequency mode: both at-risk denominators shown together (e.g. "18,2% від об'єднаної бази ·
  22,2% від активних поточних покупців"). Selected "Зовсім сплять" (Sleeping, real count 2) —
  confirmed all 3 filter labels flip to "…за попередній період"/"(попередній)" wording, no
  decline-threshold field shown, and both real Sleeping rows render `Тип. чек = "—"` (never "0").
  Selected "Купують рідше" (Declining, real count 0 in this window) — confirmed the
  decline-threshold field appears (Sleeping didn't have it), labels stay in "поточний" wording,
  and the empty-audience state renders its friendly message instead of breaking the page.

No console errors, no server errors, throughout. Stopped both dev-preview servers after
verification.

## Not in scope (per brief, unchanged)

Backend/mobile untouched. Existing RFM UI (`features/marketing-analytics/` outside
`price-segments/`) read only, not modified beyond the Sidebar/i18n additions needed for the new
nav item.

## Files touched

- `frontend/app/(dashboard)/marketing-analytics/price-segments/page.tsx` (new)
- `frontend/features/marketing-analytics/price-segments/components/AllTimeView/*.tsx` (new, 5 files)
- `frontend/features/marketing-analytics/price-segments/components/FrequencyView/*.tsx` (new, 3 files)
- `frontend/features/marketing-analytics/price-segments/api/priceSegments.ts` (1-line type fix)
- `frontend/components/layout/Sidebar.tsx` (new nav item)
- `frontend/messages/en.json`, `frontend/messages/uk.json` (full `priceSegments` namespace + 1
  sidebar label each)

## Git

Not committed — working tree left for review (repo convention: main session/user commits).
