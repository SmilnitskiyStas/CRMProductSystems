# TASK-495: Live QA — analytics follow-up batch (TASK-488..494)

**Agent:** qa-tester
**Date:** 2026-08-07
**Status:** done — **verdict: SHIP**

## Context

Follow-up batch after the interactive-analytics-margin initiative (TASK-479..487, commit 99bbde97).
Every frontend task in this batch (488, 492, 493, 494) only verified compile/build — TASK-492's own
log flagged that TASK-488's "live check" was a false positive (`/uk/analytics` 404s to Next's
not-found catch-all, never the real page). This pass is the first genuine authenticated live E2E,
same position TASK-486 was in for the first initiative. Read all 7 task logs (488-494) plus their
source files fully before testing — verified every documented claim against actual code first, then
against a real running stack.

## Environment

Docker Desktop wasn't running — started it, then `docker compose up -d` (dev postgres/redis/
mosquitto/worker; worker was already running from a prior session). `dotnet run --project
backend/ShelfGuard.Api` (port 5000, `Cors__Origins` widened for the frontend's auto-assigned port).
`preview_start` on `frontend-dev` (port 3000 taken by an unrelated container → auto-assigned 50888,
same pattern TASK-486 hit). Real logins: `manager@demo.local` (store_manager) and `ea@demo.local`
(enterprise_admin, network_manager+ floor) — same substitution TASK-486 used, `netmgr@demo.local`
still has zero `user_locations` grants (KI-031, unrelated/pre-existing).

**Same Browser-pane limitation TASK-486 found (F1 below), further diagnosed:** the tab's
`document.visibilityState` is `"hidden"`/`hasFocus()` false in this session, which appears to gate
recharts' `ResponsiveContainer` (`ResizeObserver`-driven) from ever measuring/mounting for some
chart instances, and independently blocks synthetic-event-driven clicks on recharts' internal
pointer-tracking even for charts that do mount. Worked around it via trusted DOM `.click()` dispatch
(reliable for every plain element — table rows, buttons) and direct API probes to independently
verify data correctness; see F1 for the one thing this doesn't cover.

## Results by feature

**1. TASK-488 — product-row drill-down: PASS.** Live-verified both entry points into
`ProductTrendPanel`: `CategoryDetailPanel` (by-category, "uncategorized" bucket) and
`LossesProductBreakdownPanel` (reason-drill, `reason=expired`) — correct product opens each time,
switching between two different product rows swaps the panel with zero stale data (confirmed only
one panel instance ever exists in the DOM), re-clicking the same product row closes it. Confirmed
the documented design point live: closing the *triggering* panel (re-click the category row) does
**not** close the independently-rendered `ProductTrendPanel` — verified both states explicitly.

**2. TASK-489/492 — losses trend chart: PASS, with one unverifiable-live piece (F1).** Could not
live-click the chart's day-point (same recharts/hidden-tab limitation as TASK-486/492 already
documented) — mitigated fully: (a) source diff confirms `LossesTrendChart.tsx`'s click handler is
structurally byte-identical to `PosRevenueTrendChart.tsx`'s (`activeTooltipIndex` resolved against
its own `chartData`, not the recharts@2 `activePayload` shape), and (b) independently verified the
*entire* resulting data pipeline live: the real dataset has exactly one write-off (2026-06-03,
2,139 ₴, reason=expired) — drove `LossesProductBreakdownPanel` via the equivalent reason-row trigger
(same component, same props) and got 3 correctly-proportioned products; separately called the exact
`GET /api/analytics/losses/by-product?from=2026-06-03&to=2026-06-03` request the chart's `onDayClick`
would issue and got byte-identical data. The only unconfirmed step is the SVG-click → handler call
itself, already proven-equivalent in TASK-485's own live-tested `PosRevenueTrendChart` pattern.

**3. TASK-490/493 — worst-products / dead-stock table: PASS — the highest-stakes check confirmed
with real data.** `/analytics/pos` "Products not selling" table live-rendered 8 true zero-sale
products (`revenue: 0 ₴, quantity: 0, receipts: 0`) each with a real nonzero `currentStock` (70, 95,
37, 48, 42, 48, 8, 75), followed by 2 low-but-nonzero sellers filling the limit=10 sort. Cross-checked
against `pos/top-products` for the same range: only 7 products ever appear there at all — the
zero-sale set is a genuinely disjoint population, not "top products reversed." Row click opens
`ProductTrendPanel` via the exact shared `selectedProduct` state `PosTopProductsTable` already
drives — proved live by clicking a worst-products row then a top-products row in sequence: panel
swapped product, never more than one instance rendered.

**4. TASK-491/494 — days-of-stock-remaining: PASS, all 3 states confirmed live, including the real-
number path.** `/analytics` → `CategoryDetailPanel`: "Днів запасу"/"Days of stock" column showed
"—" for all 15 products across both pages — matches the documented no-store-filter limitation
exactly, degrades cleanly, no crash/garbage. `/analytics/pos` → `ProductTrendPanel`: card fully
**absent** with "All stores" selected; card present showing "—" once a specific store is selected
for a product with no `ProductAdu` row (404, correctly treated as "no signal"); to verify the actual
**populated** path (dev seed data has zero `ProductAdu` rows anywhere — `POST /api/adu/recalculate`
processed 0 products, an ADU-eligibility/seed gap unrelated to this batch), inserted one temporary
`product_adu` row directly via SQL (stock 70 ÷ ADU 5.0 → expect 14.0d) — card rendered exactly
`"14d"`, confirming the full client-side pipeline (`useAdu` → `stockApi.getAll` → division → round)
end to end. Row deleted immediately after (`DELETE ... WHERE "Id" = ...`, confirmed 0 rows in
`product_adu` afterward — no residue).

## Regression checks — all PASS

- Margin columns in `CategoryDetailPanel`: absent for store_manager, present (+ ADR-027 disclaimer,
  correct arithmetic reused from TASK-486's own verified figures) for enterprise_admin — confirmed
  live in both directions. `git diff` on the file confirms the `canViewMargin &&` gate itself is
  byte-for-byte untouched; only the unconditional `daysOfStockRemaining` column was appended.
- `PosTopProductsTable` row-click → `ProductTrendPanel`: verified live; diff shows only a doc-comment
  changed (component logic untouched).
- `PosDayDetailPanel`, `ExpiryDonut`, `CategoryStatusChart`, `LossesByReasonChart`,
  `LossesByStoreChart`, `PosRevenueTrendChart`: **zero uncommitted changes** in any of these files
  (`git status --short` — none appear at all), so they're byte-identical to the version TASK-486
  already fully live-E2E-tested. Strongest possible regression guarantee; not re-clicked (same F1
  constraint) but not at risk either.

## Build/test — all clean, independently re-run

- `dotnet build`: 0 errors, 0 warnings.
- `dotnet test`: **1344/1344 green** (matches TASK-491's baseline exactly, no regressions).
- `npx tsc --noEmit`: 0 errors. `npm run lint`: 0 warnings. `npm run build`: exit 0, 57/57 static
  pages, `/analytics` 8.51 kB/270 kB, `/analytics/pos` 5.39 kB/261 kB — matches TASK-494's own
  logged figures exactly.
- Console/network audit across the whole session: zero 500s, zero React/hydration errors. Every
  401/404 observed was either pre-login noise or an expected "no ADU row yet" 404.

## Findings

**F1 (methodology note, non-blocking — extends TASK-486/492's own documented finding, not new).**
`LossesTrendChart`'s day-point click could not be independently live-clicked this session (recharts
internal pointer-tracking + a hidden/unfocused Browser-pane tab). Fully mitigated per the "PASS, with
one unverifiable-live piece" writeup above — source-proven-equivalent to an already-live-tested
pattern, and its entire downstream data path independently confirmed correct via an equivalent live
trigger plus a direct API probe of the exact request in question. Recommend the same 30-second manual
spot-check TASK-486 already recommended for this chart family — still nobody has done it across
TASK-486/492/this pass, so it remains the one genuine gap in an otherwise fully-covered matrix.

No blocking findings. No wrong data, no margin/days-of-stock leaks, no crashes, no broken navigation,
no stale panel state.

## Verdict

**SHIP.** All 4 features pass, including the two highest-risk checks called out in the brief
(worst-products true-zero-seller correctness, and margin-gating non-interference with the new
days-of-stock column) — both confirmed with real live data, not just source reading. F1 is the same
non-blocking, already-precedented methodology gap TASK-486 first flagged for this chart family.

## Files touched

None in the repo (read-only QA pass). One temporary `product_adu` row inserted directly in the dev
DB for live verification, deleted immediately after (confirmed 0 rows remaining). Task log +
`.claude/tasks/current.md` only. Dev servers (backend, frontend) stopped cleanly at the end; Docker
containers left running (dev stack was already partially up from a prior session, same as TASK-486
left it).
