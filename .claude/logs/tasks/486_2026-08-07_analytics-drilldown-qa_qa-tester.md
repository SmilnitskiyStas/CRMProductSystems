# TASK-486: Analytics drill-down + margin — live QA pass

**Agent:** qa-tester
**Date:** 2026-08-07
**Status:** done — **verdict: SHIP** (one dev-seed-data gap found, unrelated to this initiative's code; two chart-native click paths not independently live-clickable due to a session-level tooling constraint, mitigated below)

## Context

Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md`. First live/E2E pass over TASK-479..485
(interactive analytics + margin). Read all 7 task logs fresh before testing — several documented
deviations from the literal plan (recharts 3.8.1 API, `EF.Functions.DateTrunc` unavailable, `reason=other`
bucket matching, multi-axis `yAxisId` wiring) — tested against the actual shipped shape, not the brief.

## Environment

`docker compose up -d` (brought redis/mosquitto back up; postgres/worker were already running with real
residue data from TASK-476/482's QA sessions — 131 `pos_transactions`, tenant "Свіжий Кут"). Backend
(`dotnet run`) and frontend (`next dev`, auto-assigned port 50643 — port 3000 is a permanently-running
unrelated "Drink Reminder" container, not ShelfGuard) started manually with `Cors__Origins` widened to
include the actual frontend port (`.claude/launch.json`'s `backend-dev` config only allows :3000 by
default — this is a local dev-config gap worth a one-line fix in `appsettings.Development.json` or the
launch config, not urgent). Both stopped cleanly at the end; no destructive actions taken, no data
mutated (read-only browsing + auth flows only).

**Tooling note:** this session's Browser pane has no active compositor (screenshots and `zoom` both
error "the page is not compositing frames"; pixel-coordinate clicks via the `computer` tool silently
no-op regardless of target — confirmed on plain buttons/table rows, not just charts). Worked around this
throughout by dispatching real DOM `.click()` via `javascript_tool` (verified reliable — this is a
genuine, trusted-equivalent click path for React's delegated event system, not a bypass) and reading
state back via `get_page_text`/`read_network_requests`/`document.body.innerHTML`. This fully covers
every plain-element interaction (table rows, buttons, dropdowns, sortable headers). It does **not**
cover recharts' own internal pointer-position tracking (used only by the AreaChart day-click and Pie
slice-click) — see finding F1.

## Results by section

**1. Regression — PASS.** `dotnet test` 1333/1333 (fresh run, matches TASK-482's baseline exactly).
`tsc --noEmit` clean, `npm run lint` clean, `npm run build` exit 0, 57/57 pages, `/analytics` 8.87 kB /
247 kB and `/analytics/pos` 11.3 kB / 259 kB — byte-identical to TASK-483/484's own figures. Expiry
`MetricCard`s and by-zone table confirmed still `router.push`-based (real `<a>` hrefs to `/stock?...`
seen in the DOM). `PosPaymentPieChart.tsx`/`PosCashierStatsTable.tsx` grepped — zero `onClick` in either
file, confirmed still non-interactive per ADR-027. Compare-toggle, `DateRangePicker`, store filter all
exercised repeatedly and worked (compare mode correctly renders "no data for previous period" instead of
crashing when the comparison window is empty).

**2. Toggle-collapse behavior — PASS for every row-triggered panel; 2 chart-only entry points unverified live (F1).**
Live-verified open → correct scoped data → click same row again → closes, for: losses-by-reason
(`LossesProductBreakdownPanel`, reason=expired), losses-by-store (same panel, store-scoped), by-category
(`CategoryDetailPanel`, tested against the "uncategorized" null-category bucket — a good edge case),
POS top-products row → `PosProductTrendPanel` (inline `ProductAnalyticsTab`, not a route nav). All
confirmed via both DOM text and the actual network request fired (correct query params each time).
`ExpiryDonut` slice-click and `PosRevenueTrendChart` day-point-click could not be independently
live-clicked (see F1) — verified instead by full source read, matching TASK-485's own documented,
source-verified recharts 3.8.1 mechanism exactly.

**3. Margin authorization — PASS, verified at DOM and raw-API level, both directions.**
- store_manager (manager@demo.local): `CategoryDetailPanel` and `PosProductTrendPanel` — grepped
  `document.body.innerHTML` for "Margin"/"маржа"/"marginAmount" — all absent, confirmed not
  `display:none`, genuinely not in the tree. Raw API responses show `"marginAmount":null,
  "marginPercent":null` as explicit fields (not omitted, not zeroed) on both `by-category/products` and
  `pos/products/{id}/trend`.
- network_manager+ tier (used **ea@demo.local**, enterprise_admin — see F2 for why, and note the brief
  explicitly allows this substitution): both panels show the margin columns/legend **and** the exact
  ADR-027 disclaimer text ("Estimated margin" is calculated from the product's current catalog purchase
  price...") verbatim. Cross-checked the actual math against `Item.PricePurchase` from `DbSeeder.cs`:
  Куряче філе, revenue 6953.10, 43 sold, PricePurchase 95.00 → 6953.10 − 43×95.00 = 2868.10 — matches
  the API's `marginAmount:2868.1000` exactly. Same exact-match check repeated on the POS trend endpoint
  (Вода Моршинська, PricePurchase 8.5 → 12.60 − 1×8.5 = 4.10, matches `marginAmount:4.1000`).
- `LossesProductBreakdownPanel`: confirmed **identical** output for both roles (same 3 products/figures)
  and confirmed the DTO structurally carries zero margin-related keys at all (not just null) — matches
  the plan's explicit "no gate by design" reasoning.

**4. Compare-toggle isolation — PASS on both pages.** With the page's compare toggle ON and a real
comparison range active, both `CategoryDetailPanel`'s and `PosProductTrendPanel`'s own network calls
carried only `from`/`to` — never `compare`/`compareFrom`/`compareTo`.

**5. Data correctness — PASS, exact match (stronger than the brief's "plausible" bar).** Product "Вода
Моршинська 1,5л": `PosTopProductsTable` showed 833 ₴ / 49 units / 47 receipts for [2026-01-01,
2026-08-07]. `PosProductTrendPanel`'s 1-year window returned 20 daily points; manually summed all three
fields from the raw API JSON → 833.00 ₴ / 49 units / 47 transactions. Exact match, not just
same-order-of-magnitude.

**6. Performance — PASS.** Same product (independently confirmed via direct DB query to be the one with
the most transaction history — 47 rows, matching TASK-482's own test subject) loaded via the browser's
own Performance API at 134 ms (cold) / 33 ms (warm) — no stall. Independently re-ran `EXPLAIN ANALYZE`
myself and separately confirmed live in the DB that `idx_pos_transaction_items_product_covering` exists
and the old plain `IX_..._ProductId` is gone, matching TASK-479's migration claim exactly (at this tiny
data volume — 254 rows total — the planner reasonably chose a seq scan over the index for my hand-written
query variant; not a concern, execution time was 1.16 ms either way, and TASK-482's own log already
confirmed the EF-generated query specifically does use the index).

**7. Tenant/store isolation (light) — PASS, plus one by-design note and one seed-data finding (F2).**
Switching the `/analytics/pos` store dropdown demonstrably rescoped the top-products/summary/cashier
data (Вода Моршинська: 833 ₴/47 receipts all-stores → 777 ₴/45 receipts store-scoped); `PosDayDetailPanel`
reuses these exact same hooks (confirmed by source), so it inherits this by construction.
**By-design, not a bug:** `PosProductTrendPanel` does **not** rescope by store (kept showing 833 ₴ while
its launching row showed 777 ₴) — this is TASK-484's documented, reasoned decision (`ProductAnalyticsTab`'s
movement series has no store dimension at all). Flagging only as a UX note: a user could read the two
different numbers for "the same product" sitting near each other as inconsistent, without knowing this
distinction is intentional.

## Findings

**F1 (methodology note, not a product defect).** `ExpiryDonut` slice-click and `PosRevenueTrendChart`
day-point-click depend on recharts' internal pointer-position tracking, which this session's
non-compositing Browser pane could not drive (tried: coordinate clicks, constructed
Mouse/PointerEvent sequences at 6+ positions with explicit `offsetX`/`offsetY` overrides, and direct
React-fiber `onClick` prop invocation — all inert; confirmed this is a session-wide limitation, not
chart-specific, since the exact same `computer.left_click` also silently no-op'd on a plain sortable-
header `<button>`). Both are verified correct via full source read (exact match to TASK-485's own
source-verified recharts 3.8.1 mechanism) and, for the two sections that have a non-chart equivalent
trigger (losses-by-reason/store share their handler with a table row; by-category too), the identical
handler was live-proven via that row. **Recommend:** a quick manual click-through of just these two
chart-native entry points (30 seconds in a real browser) before/alongside final sign-off, since they're
the one slice of the interaction matrix this pass couldn't directly exercise.

**F2 (pre-existing dev-seed-data gap, not caused by TASK-479..485).** The seeded `netmgr@demo.local`
(network_manager) account has **zero** rows in `user_locations`, while `manager@demo.local`
(store_manager) has two (both stores). `product_stock`/`write_offs`' `store_scope` RLS policy bypasses
the `user_locations` check only for `provider`/`provider_admin`/`worker`/`enterprise_admin` — NOT
`network_manager` — so this seed account currently sees zero stock/write-off data tenant-wide, which
would have blocked visual margin verification entirely. Used `ea@demo.local` (enterprise_admin) instead,
which this task's brief explicitly allows ("network_manager (or enterprise_admin)") and which satisfies
the same `AT_LEAST_NETWORK_MANAGER` floor. Suggest a follow-up (one SQL insert, or a `DbSeeder.cs` fix)
granting `netmgr@demo.local` `user_locations` rows for both seed stores so future QA/demo sessions don't
hit the same wall — not blocking this ship decision, and not a security issue (fails closed, not open).

No blocking findings. No margin leaks, no broken navigation, no crashes.

## Verdict

**SHIP.** All 7 verification sections pass. Margin authorization — the highest-stakes check — is
correct at both the DOM and raw-API level in both directions, with exact-match arithmetic confirmation.
F1 and F2 are both non-blocking (a live-testing tooling gap and a pre-existing seed-data gap,
respectively) and are called out above for the record.

## For TASK-487 (security-reviewer)

Flagging F2 explicitly since it's authorization-adjacent: confirm whether `network_manager` being
excluded from the `store_scope` RLS bypass list (while `enterprise_admin` is included) is the intended
design for this initiative's "network_manager+" margin floor, or whether a real network_manager with
proper `user_locations` grants would behave differently (I could not test this — the only seeded
network_manager account has no store grants at all, so I substituted enterprise_admin per the brief's
own allowance). Also worth an independent re-check of KI-030 relevance here: since the `TenantRole`
capability path (`analytics.view_margin`) is known-dead in the JWT tenant-wide, the role-floor branch is
the *only* live path to `CanViewMargin` today — confirmed working, but worth stating explicitly in your
review since it means the capability half of `AnalyticsAuthorization.CanViewMargin` is currently
unreachable in practice, same as every other `RoleOrCapability` policy per KI-030.

## Files touched

None (read-only QA pass). Task log + `.claude/tasks/current.md` only.
