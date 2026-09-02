# TASK-673 (QA): supplier metrics detail page — verification + regression

**Agent:** qa-tester · **Date:** 2026-09-02 · Feature: TASK-670..672 (`e01bd61f`, `7f43a496`,
`c7ffdf53`), docs `1db8ffd8`. Main working tree, HEAD `1db8ffd8`. Verification only — no feature
code changed.

## VERDICT: SHIP

All automated checks green at baseline. Full E2E exercised against a live dev stack (API :5080,
frontend dev :3007, dev DB `crmproductsystems-postgres-1`, worker job via BullMQ). No blockers, no
high/medium bugs. One low nit (orphaned i18n keys) + two informational notes.

---

## Automated checks

| # | Check | Result |
|---|---|---|
| 1 | `dotnet build ShelfGuard.sln` | **0 errors, 1 warning** — the known pre-existing CS8602 in `MarketplaceServiceTests.cs` (full-rebuild only). PASS |
| 2 | `dotnet test ShelfGuard.sln` | **2174 / 2174 passed, 0 skipped, 0 failed** — exact baseline. PASS |
| 2b | Targeted filters `MarketplaceServiceTests` + `MarketplaceRepositoryMetricsHistoryIntegrationTests` + `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass` | **81 / 81 passed.** The ForceRls triad test passes with `supplier_metrics_snapshots` present. PASS |
| 3 | `tsc --noEmit` / `next lint` / `vitest run` / `next build` | tsc clean · lint clean · **vitest 59/59** · `next build` exit 0, route `ƒ /marketplace/[id]/metrics 8.94 kB / 233 kB` present. PASS |
| 4 | uk/en deep-key parity | **4652 == 4652**, 0 keys drift either direction. PASS |
| 5 | `worker` tsc / `mobile` tsc | both clean. PASS |
| 6 | `dotnet ef migrations has-pending-model-changes` | "No changes have been made to the model since the last migration." PASS |
| 7 | psql — `supplier_metrics_snapshots` policies | 3 policies (`tenant_isolation` NULLIF-guard, `provider_bypass` IN ('provider','provider_admin'), `worker_bypass` = 'worker'), all `cmd=ALL`, no `with_check`. `relrowsecurity=t`, `relforcerowsecurity=t`. Column types mirror `supplier_metrics` verbatim. PASS |

---

## E2E

Stack: `dotnet run ShelfGuard.Api --urls http://localhost:5080` (dev DB :5435), `next dev -p 3007`
with `NEXT_PUBLIC_API_URL=http://localhost:5080` (CORS origin added via `Cors__Origins` env),
worker job run through the `qa673-run-snapshot.ts` BullMQ harness against dev Redis :6380.
Test supplier `b4e21658-13b7-44d2-8924-4fd1aa5105d3` (public, tenant `f1bbc48c…`), buyer
`ea@demo.local` / «Свіжий Кут» (tenant `8abfbbb5…`).

### 1 — Worker snapshot write — PASS
- Ran `supplier-metrics-recompute` job: log `suppliers: 11 … snapshots written: 11`. One
  `supplier_metrics_snapshots` row per profiled supplier for `CURRENT_DATE` (2026-09-02).
- Re-ran → row count unchanged (11 for today), `CreatedAt` on the test supplier's row **unchanged**
  (`ON CONFLICT DO UPDATE` keeps `Id`/`SnapshotDate`/`TenantId`/`CreatedAt`, overwrites the 8 metric
  columns). Idempotent.
- Snapshot `Rating` (4.00) == live `supplier_metrics.Rating` (4.00); `QualityScore` NULL in both
  (read-back, job computes neither). Confirmed against DB.

### 2 — History endpoint — PASS (curl, buyer JWT)
- `GET …/metrics-history` → 200, 11 points, `date` ascending oldest→newest, `qualityScore:null`
  preserved.
- `?days=5` → clamped to 7 → 2 rows (fewer). `?days=0` → clamped to 7 → 200 + 2 rows (still
  returns). `?days=40` → 11 rows, first = 2026-07-24 (40-day cutoff inclusive). `?days=99999` →
  clamped to 365 → 11 rows.
- Unknown GUID → **404**. Unpublished supplier (`316a8df3…`, `IsPublic=false`) → **404**. No
  `Authorization` header → **401**.
- Browser: `metrics-history?days=90` — CORS preflight `OPTIONS → 204`, `GET → 200`.

### 3 — Profile tiles → detail — PASS
- `/marketplace/{id}`: all 6 metric tiles are `next/link`s. Clicking "Average delivery time" →
  `/marketplace/{id}/metrics#delivery`, page scrolls to the delivery section. Clicking "Rating" →
  `…#rating`, page at the rating section.
- "More about these metrics →" → `/marketplace/{id}/metrics` (no anchor). "‹ to profile" back link
  → `/marketplace/{id}`. All work.

### 4 — Detail content — PASS
All 7 sections render current value + explanation:
- rating `4.0` + StarRating, chart Y locked `[0,5]`.
- delivery `2.6 d.` + "based on 48 orders", chart auto Y `2.1–3.5`; **By region** table:
  Житомирська `Declared 1–3 d. · from 5000 UAH` / `Actual 1.8 d. n=25`; м. Київ (UA-30) &
  Київська (UA-32) `Declared —` / measured. Declared-vs-measured comparison correct, incl. "—" for
  undeclared measured regions.
- accuracy `97%`, chart % auto-domain. quality `—` + **empty state** "The trend will appear after a
  few nightly recalculations" (QualityScore all-null → <2 points). response `5.8 h.` + "based on 12
  inquiries". cancellation `4%`.
- coverage section: explanation + `SupplierCoveragePanel` (declared region + "Does not deliver to"
  + note).
- Charts use Recharts' standard mount animation (~1–2 s to paint the area) — same as the
  `LossesTrendChart` they were cloned from; not a defect.
- No console errors on the metrics page.

### 5 — Removed collapsible — PASS
Profile `SupplierMetrics` has no "details by region" / "детальніше по регіонах" toggle and no
inline `DeliveryByRegionPanel`; no console error. `DeliveryByRegionPanel` still exists and is
imported by the detail page as the fallback when `deliveryCoverage.served` is empty.

### 6 — RLS — PASS
`SET app.tenant_id` = buyer «Свіжий Кут» → `SELECT` from `supplier_metrics_snapshots` for the
test supplier (other tenant) → **0 rows**. Owner tenant → 10. `SET app.role='provider'` → 10.
The endpoint still returns full history for the buyer (service resolves via the provider RLS
override, same pattern as `/coverage`, pure LINQ — KI-036/ADR-035 intact).

### 7 — Mobile (static) — PASS
No `mobile/` changes in TASK-670..673 (`git diff` empty). `mobile` tsc clean.

---

## Regression

- **Marketplace flows** — profile page loads catalog, reviews, coverage panel, chat, cooperation
  (`/api/marketplace/cooperation → 200`) unaffected. Browse/profile render normally.
- **Nightly `supplier_metrics` write-boundary** — `UPSERT_METRICS_SQL` DO-UPDATE list unchanged;
  `Rating` / `QualityScore` / `UpdatedAt` appear nowhere in it. The new `SNAPSHOT_UPSERT_SQL`
  targets the separate append-only `supplier_metrics_snapshots` (nothing else writes it), copying
  Rating/QualityScore there — intended per ADR-036 amendment.
- **`ShelfGuard.Tools.DeliveryCoverageBackfill`** — dry-run on dev: 1 row scanned, 0 updates (the
  one candidate is `DeliveryRegions=[]` → nothing to map). `--apply` is a no-op on dev →
  idempotent.

---

## Findings

### NIT-1 (low) — orphaned i18n keys
`Dashboard.marketplace.metrics.regionsToggleShow` / `regionsToggleHide` are now dead — TASK-672
removed their only consumer (the inline toggle in `SupplierMetrics.tsx`). Still present in **both**
`frontend/messages/uk.json:3227-3228` and `en.json:3227-3228`, so key parity is unaffected and
there is no runtime impact. Cosmetic cleanup only.

### NOTE-1 (info) — recompute job nulls live metrics without source data
Running `supplier-metrics-recompute` on dev overwrote the test supplier's live
`supplier_metrics.AvgDeliveryDays` (2.30 → NULL) because dev has no delivery-sample rows in the
window. This is the job's normal full-recompute behaviour (pre-dates TASK-670..672), not a
regression. Re-seeded for the UI pass via `scripts/qa/673_seed_live_metrics.sql`.

### NOTE-2 (info) — dev DB left with QA seed data
`scripts/qa/673_seed_metrics_history.sql` (10 historical rows for the test supplier, 2026-07-24 →
2026-08-29) + `scripts/qa/673_seed_live_metrics.sql` (rich live metrics) + the 2026-09-02 rows the
worker job wrote for all 11 profiled suppliers are left in place. Both seed scripts are idempotent
and committed. `supplier_metrics_snapshots` now holds 31 rows / 12 distinct dates.

### NOTE-3 (info) — uk locale not live-retested this run
The dev env exposes no language switcher and `NEXT_LOCALE` cookie did not flip next-intl; live UI
pass was EN only. uk coverage rests on: clean deep-key parity, all 16 new `metricsPage.*` keys
present with proper Ukrainian text in the commit, and TASK-672's own log ("live-verified" uk).
Low risk.

---

## Artifacts
- Seed / harness: `scripts/qa/673_seed_metrics_history.sql`, `scripts/qa/673_seed_live_metrics.sql`,
  `worker/qa673-run-snapshot.ts`.
- Screenshots (described, not saved to repo): profile metric tiles (6, all links); detail page
  header + Rating section + trend chart (orange area, Y 0–5); delivery section + "By region"
  declared-vs-actual cards; Product-quality empty state.
