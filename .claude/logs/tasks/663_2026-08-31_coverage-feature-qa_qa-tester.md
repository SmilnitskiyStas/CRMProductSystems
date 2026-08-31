# TASK-663 (T16) — e2e + regression QA: supplier delivery-coverage / performance-metrics

**Agent:** qa-tester · **Date:** 2026-08-31 · **Status:** done
**Scope under test:** TASK-648..662 (plan `eventual-whistling-rabbit.md`), merged to `main` HEAD `f11425cd`.
**Verdict: SHIP WITH NOTES** — all automated checks green, all 8 e2e steps + all regression checks pass.
One LOW-severity UX wording nit in the cooperation modal (BUG-1 below); non-blocking, advisory-only panel.

Verification task — no feature code changed. Dev data seeded then cleaned (see "Data" section).

---

## 1. Automated checks

| Check | Result |
|---|---|
| `dotnet build ShelfGuard.sln` (clean) | **PASS** — 0 errors, 1 warning = known pre-existing `CS8602` at `ShelfGuard.Tests/Marketplace/MarketplaceServiceTests.cs:875` |
| `dotnet test ShelfGuard.sln` (full) | **PASS — 2134 / 2134**, 0 failed, 0 skipped |
| RLS audit `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass` | **PASS** (ran in full suite + re-run in isolation) — no new tables |
| Coverage-feature test classes (targeted filter: `DeliveryCoverageJsonTests`, `DeliveryRegionsBackfill`, `MarketplaceRepositoryCoverageFilterIntegrationTests`, `UkraineRegionsTests`, `GeoServiceTests`, `GeoControllerTests`, `ContractPdfGeneratorTests`, `SupplierAgreementServiceTests`, `SupplierCabinetServiceTests`, `MarketplaceOrderServiceTests`, `DeliveryCoverageBackfill`) | **PASS — 185 / 185** |
| `frontend`: `npx tsc --noEmit` | **PASS** (clean) |
| `frontend`: `npm run lint` | **PASS** — "No ESLint warnings or errors" |
| `frontend`: `npx vitest run` | **PASS — 50 / 50** (7 files) |
| i18n `uk.json` / `en.json` deep-key parity | **PASS** — 4611 == 4611, 0 drift |
| `mobile`: `npx tsc --noEmit` | **PASS** (clean) |
| `worker`: `npm run build` (`tsc`) | **PASS** (clean) |
| EF `migrations has-pending-model-changes` | **PASS** — "No changes have been made to the model since the last migration" (snapshot consistent) |

**Migration state (dev DB `crmproductsystems-postgres-1:5435`):** `20260831090731_AddSupplierPerformanceData`
applied. 7 columns present (`locations.RegionCode`, `marketplace_orders.DestinationRegionCode`,
`supplier_profiles.DeliveryCoverage`, `supplier_metrics.{DeliveryByRegion,DeliverySampleSize,ResponseSampleSize,AggregatesComputedAt}`).
`pg_policies` on `locations` / `marketplace_orders` / `supplier_profiles` / `supplier_metrics` —
**identical** to before: 3 policies each (`tenant_isolation`, `provider_bypass`, `worker_bypass`).

---

## 2. E2E (API `:5000` + web `:3009` w/ `Cors__Origins` extended for the run + dev DB + real BullMQ worker code)

Supplier tenant `supplier-alpha` (`f1bbc48c…`, supplier id `b4e21658…`, FREE plan) · buyer `ea@demo.local` / «Свіжий Кут» (`8abfbbb5…`). Password fixture `password`.

| # | Step | Result | Evidence |
|---|---|---|---|
| 1 | Supplier declares coverage (`DeliveryCoverageEditor` on `/supplier/profile`) | **PASS** | Edited note + `Київська` terms in the editor → `PUT /api/supplier-cabinet/profile` **200** → reload: editor re-hydrates persisted values (`termInputs=["1-2 дні, від 3000 грн","2-3 дні, від 2000 грн (QA-663)"]`, note persisted). Ukrainian stored clean in jsonb. `notServed` region (`UA-43` / Крим) is rendered **disabled** in the "Served" checklist — mutual exclusivity enforced. |
| 2 | Buyer filters marketplace by region | **PASS** | UI dropdown (`RegionSelect`, populated from `GET /api/geo/regions`, oblasts + cities as `<optgroup>`). No filter → 3 suppliers. `?regionCode=UA-30` → **1** (alpha only). `?regionCode=UA-43` → **0** ("Список постачальників порожній") — alpha excluded by its `notServed`. API cross-check matches exactly. |
| 3 | Supplier profile page (buyer view) | **PASS** | `SupplierCoveragePanel` always visible on a FREE-plan supplier (not premium-gated): served `м. Київ` / `Київська` + terms, "Не доставляє: Автономна Республіка Крим" (codes → UA names), note. Metric tiles: "Середній термін доставки 3 дн. · на основі 1 замовлень", "Час відповіді 0 год. · на основі 1 звернень", "Точність замовлень 100%", "Якість товарів —". Drill-down toggle → `DeliveryByRegionPanel`: "м. Київ · 3 дн. · n=1". (First-run "недостатньо даних" empty state was verified in TASK-656; here it correctly shows data after the worker run.) |
| 4 | Cooperation modal coverage panel | **PASS (see BUG-1)** | `CooperationCoveragePanel` renders at top of the request modal; calls `GET /api/marketplace/suppliers/{id}/coverage`; CORS preflight OK; picking a region in the override `RegionSelect` re-fires `…/coverage?buyerRegionCode=UA-32` **200**. Advisory only — "Подати заявку" stays enabled. served / not_served / unknown rendering also verified directly against the API and (exhaustively) in TASK-657. |
| 5 | Contract PDF | **PASS** | Client submits request → supplier seeds contract settings (LegalName + Iban + ServiceName) → supplier approves → `GET /api/marketplace/cooperation/{id}/contract` **200**, valid PDF, 2 pages. New section **"5. РЕГІОНИ ТА УМОВИ ДОСТАВКИ"**: 2-col table `м. Київ / 1-2 дні, від 3000 грн` + `Київська / 2-3 дні`; "5.2. Доставка не здійснюється в такі регіони: Автономна Республіка Крим."; "5.3. Доставка Новою Поштою за домовленістю". Signatures renumbered to **"6. ПІДПИСИ СТОРІН"**. All region names render in correct Ukrainian (DejaVu) — **no □□□**. Screenshot + extracted text in `663_screenshots/`. |
| 6 | Order lifecycle → region snapshot | **PASS** | Agreement → Active. Buyer places order with `destinationStoreId` = "Свіжий Кут Центральний" (`RegionCode=UA-30`, set via `PUT /api/locations/{id}` — invalid code `ZZ-99` correctly 400'd). `marketplace_orders` row: **`DestinationRegionCode = UA-30`** (snapshot at creation). Supplier confirm → ship (`EstimatedDeliveryDays=2`) → `ShippedAt` set. Client receipt create → scan item → finalize → **`Status = delivered`, `DeliveredAt` set**. |
| 7 | Worker `supplier-metrics-recompute` | **PASS — incl. the critical write-boundary check** | Ran the real compiled job via BullMQ (`startSupplierMetricsRecomputeWorker` + one enqueued job). Log: `suppliers: 8, with delivery data: 1, with response data: 3, region rows: 1`. alpha `supplier_metrics`: `AvgDeliveryDays=3.00`, `DeliverySampleSize=1`, `DeliveryByRegion=[{"regionCode":"UA-30","sampleSize":1,"avgDeliveryDays":3}]`, `ResponseTimeHours=0.00`, `ResponseSampleSize=1`, `CancellationRate=0.0000`, `OrderAccuracy=1.0000`, `AggregatesComputedAt` bumped. **`Rating` = 4.00 UNCHANGED. `UpdatedAt` = 2026-07-03 06:02:40.251558+00 UNCHANGED. `QualityScore` still NULL.** All 8 metrics rows: `Rating`/`UpdatedAt` untouched, only aggregate columns + `AggregatesComputedAt` written. Buyer profile tiles + `…/coverage` (`measuredAvgDeliveryDaysToBuyerRegion=3, measuredSampleSize=1`) then reflect the values. |
| 8 | Mobile `mobile/app/(app)/marketplace/[id].tsx` (static read — no emulator, standing constraint) | **PASS** | `DeliveryCoverageBlock` (served + terms/"за домовленістю", muted "Не доставляє: …", note) gated on `profile.deliveryCoverage`. `DeliveryByRegionList` collapsible, sorted asc, `regionLabel · N дн. · n=N`, empty → "Ще недостатньо даних по регіонах". NEW "Час відповіді" tile: `responseTimeHours` + " год.", `fallback="недостатньо даних"`, sublabel "на основі N звернень". **KI-037 fix present**: `Math.round(m.orderAccuracy * 100)` / `Math.round(m.qualityScore * 100)` (the star display's `Math.round(value)` is the 0–5 rating, correct). `features/geo/{api,hooks,types}.ts` + `features/marketplace/types.ts` (`deliveryByRegion`, `deliverySampleSize`, `responseSampleSize`, `aggregatesComputedAt`, `deliveryCoverage`) all wired to the same DTO fields as web. |

---

## 3. Regression

| Check | Result |
|---|---|
| Legacy supplier (only `Region` / `DeliveryRegions`, `DeliveryCoverage IS NULL`) still in unfiltered marketplace list | **PASS** — `fe-chat-supplier` set to `Region='Львівська'`, NULL coverage → present in unfiltered list (3 total) |
| …and matches a region filter via `Region ILIKE` fallback pre-backfill | **PASS** — `?regionCode=UA-46` → present; `?regionCode=UA-30` → absent |
| `ShelfGuard.Tools.DeliveryCoverageBackfill --apply` converts `DeliveryRegions` → `DeliveryCoverage.served` codes | **PASS** — `["Львівська область","Львів"]` → `{"served":[{"regionCode":"UA-46"},{"regionCode":"UA-46-LVIV"}],"notServed":[]}`; dry-run + apply both clean; `[skip]` for empty `DeliveryRegions` |
| …and the backfilled supplier then matches the region filter via the structured `served` path | **PASS** — still in unfiltered list (3) + `?regionCode=UA-46` → present |
| Supplier profile page does not crash on null `deliveryCoverage` | **PASS** — `fe-chat-supplier` (null coverage) profile page renders "Постачальник не вказав регіони доставки", metrics "на основі 0 замовлень", no console/React errors |
| Contract PDF generation does not crash on null `deliveryCoverage` | **PASS** — `ContractPdfGeneratorTests.Generate_NullDeliveryCoverage_OmitsSection_KeepsSignatures` green; section omitted, signatures still render |
| Existing marketplace flows (browse, cooperation request, order lifecycle, receipt, chat) still work | **PASS** — exercised end-to-end in step 5/6 |
| `GET /api/marketplace/suppliers` with **no** region filter returns the same set as before | **PASS** — 3 public suppliers, unchanged |
| `next-intl` missing-key throw during the whole browser session | **PASS** — none; only expected 401s from the mid-session logout |

---

## 4. Bugs

### BUG-1 — cooperation-modal "unknown" coverage message is misleading when the buyer's region *is* resolved — LOW
- **File:** `frontend/features/marketplace/components/CooperationCoveragePanel.tsx` (~L96) + i18n key
  `Dashboard.marketplace.cooperationRequestModal.coverage.regionUnknown` ("Не вдалося визначити ваш регіон").
- **Root cause:** the panel branches only on `buyerRegionStatus` (`served|not_served|unknown`). The backend
  (`MarketplaceService.GetSupplierCoverageForBuyerAsync`) returns `"unknown"` for **two** different situations:
  (a) `buyerRegionCode == null` — the buyer's region genuinely can't be resolved; and
  (b) `buyerRegionCode` **is** resolved (e.g. `"UA-30"`) but the supplier declared it in neither `served`
  nor `notServed`. The `SupplierCoverageForBuyerDto.BuyerRegionStatus` enum has no value to tell (b) apart from (a).
- **Repro:** buyer «Свіжий Кут» with primary `Location.RegionCode = UA-30`, open the cooperation modal for a
  supplier whose coverage does not mention `UA-30` (e.g. `fe-chat-supplier` with empty coverage, or `supplier-alpha`
  viewed from a `UA-46` buyer).
- **Expected:** something like "Постачальник не вказав, чи доставляє у ваш регіон (м. Київ)".
- **Actual:** "Не вдалося визначити ваш регіон" + a `RegionSelect` prompt to re-pick the region that is already known.
- **Impact:** cosmetic only. The panel is advisory and never blocks submission; the happy path
  (`supplier-alpha` + `UA-30` buyer → `served` + terms) renders correctly. Not a ship blocker.
- **Fix sketch:** either add a 4th `BuyerRegionStatus` (`"region_not_declared"`) or have the panel check
  `buyerRegionCode != null` before showing `regionUnknown`.

No other bugs found. No data-integrity, tenant-isolation, RLS, or write-boundary issues.

---

## 5. Data touched on dev DB (all seeded via the feature's own APIs / the backfill tool, then reverted)

- `locations` "Свіжий Кут Центральний" `RegionCode` → `UA-30` — **kept** (realistic, needed for the buyer-region feature).
- `supplier_profiles` (alpha) `DeliveryCoverage` — edited via UI during step 1, then **restored** to the original
  (`served UA-30/UA-32`, `notServed UA-43`, original note).
- Cooperation agreement + contract settings + marketplace order + receipt + chat thread + auto-provisioned item
  + stock/movements + queued notifications — **all deleted**.
- `supplier_metrics` (alpha) aggregate columns — **reset to baseline** (`AvgDeliveryDays/DeliveryByRegion/…` NULL,
  sample sizes 0); `Rating`/`UpdatedAt` were never touched. `AggregatesComputedAt` left bumped (harmless).
- `supplier_profiles` (`fe-chat-supplier`) — regression scenario set then **restored** to all-NULL.
- `ShelfGuard.Tools.DeliveryCoverageBackfill --apply` was run (regression check) — it only touched the temporary
  `fe-chat-supplier` row, which was then restored. `qa-test-supplier` (converted by TASK-661) untouched.
- Redis `bull:supplier-metrics-recompute*` temp keys — **deleted**.
- No feature source files changed. Temp runner `worker/run-metrics-job.mjs` — **deleted**.

## 6. Not done

- Emulator run of the mobile screen (standing TASK-435 constraint — no AVD). Static review + `tsc` only.
- `backend/openapi.json` regen — pre-existing pending chore (KI-040), out of scope for QA.
