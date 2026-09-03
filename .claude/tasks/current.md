# Активна робота — Marketplace: delivery-coverage + performance-metrics (серп.–вер. 2026)

Поточний кластер: **TASK-648..673** — geo-регіони → структуроване delivery-coverage
→ метрики постачальника + нічні snapshot-и → buyer-сторінка metrics detail.

Усе від **TASK-647** і старіше винесено в `.claude/tasks/archive/` (розбито за
спринтами). Для старих задач — `grep` по TASK-ID в `archive/`. Історія — в git.

## TASK-674 — Provider-керовані модулі `mobile_app` + `analytics`

**Status:** review (не запушено) · **Agent:** main session · **Plan** `peaceful-chasing-piglet.md` · **ADR-037**
Log: `.claude/logs/tasks/674_2026-09-02_mobile-app-analytics-modules_main-session.md`

Розділи «Застосунок» (`consumer_app`) і «Аналітика» (`analytics`) показувались усім тенантам
без огляду на модулі. Додано 2 нові ключі в `Tenant.UpdateModules` allow-list: `mobile_app`
(весь розділ «Застосунок» + 12 контролерів class-level + 4 `customer-messages` екшени
`NotificationsController`), `analytics` (14 з 17 екшенів `AnalyticsController` поштучно —
дашборд-home і Events-календар лишились без гейта). `loyalty` не чіпали (лише каса).
**Без backfill, default-off** — після деплою наявні тенанти втрачають обидва розділи, поки
Провайдер не увімкне вручну (breaking, узгоджено). Frontend: 3 списки ключів, `Sidebar`
`moduleKey` на 2 групи, новий `ModuleGate` + 2 nested `layout.tsx`, i18n uk/en. Build/tsc
чисто, `dotnet test` 309/309 (RequireModule+Analytics). Browser E2E не проганявся.
Далі: regen `openapi.json`; shopper-app при знятті `mobile_app` — окрема задача.

## TASK-673 (QA) — verification + regression for supplier metrics detail page

**Status:** done · **Agent:** qa-tester · **Verdict: SHIP.** Verification only, no feature code changed.
Log: `.claude/logs/tasks/673_2026-09-02_metrics-detail-page-qa_qa-tester.md`

Feature TASK-670..672 (`e01bd61f` / `7f43a496` / `c7ffdf53`), docs `1db8ffd8`. Automated: build 0
err / 1 known warn, `dotnet test` **2174/2174** (0 skipped), ForceRls-triad test green with
`supplier_metrics_snapshots`, tsc/lint clean, vitest 59/59, `next build` OK (route present),
uk/en parity 4652==4652, worker+mobile tsc clean, ef `has-pending-model-changes` none, 3 RLS
policies + FORCE RLS on the new table. E2E on a live dev stack (API :5080 / next :3007 / worker
BullMQ harness): worker snapshot write idempotent + Rating/QualityScore read-back correct;
`metrics-history` endpoint clamp `[7,365]` + 404 (missing/unpublished) + 401 verified; profile
tiles deep-link to `/metrics#anchor` and scroll; all 7 detail sections render (current value +
explanation + trend chart, quality→empty state, delivery→declared-vs-measured by-region table,
coverage→SupplierCoveragePanel); profile region-toggle removed cleanly; cross-tenant RLS isolates
the snapshot table while the endpoint still serves buyers via the provider override. Regression:
marketplace flows, `supplier_metrics` write-boundary, DeliveryCoverageBackfill idempotency — all
clean. Findings: 1 low nit (orphaned `metrics.regionsToggleShow/Hide` i18n keys, parity intact) +
3 info notes (recompute nulls live metrics w/o source data — pre-existing; dev left with QA seed
data; uk not live-retested this run — covered by parity + commit review). Not pushed.

## TASK-673 (docs) — document supplier metrics history + detail page

**Status:** done · **Agent:** documentation-writer · Docs only.
Log: `.claude/logs/tasks/673_2026-09-01_metrics-detail-page-docs_documentation-writer.md`

Extended ADR-036 with 2026-09-01 amendment explaining TASK-670..672: separate append-only
`supplier_metrics_snapshots` table design, no concurrency risk with live metrics, charts empty until
≥2 snapshots (known issue KI-043). Added `SupplierMetricsSnapshot` to domain-model; documented
`GET /api/marketplace/suppliers/{id}/metrics-history` endpoint in api-contracts; verified TASK-670
section complete in database-schema; added KI-043 (trend charts warm-up + QualityScore null) to
known-issues. All files bumped to 2026-09-01. Docs-only commit (not pushed).

## TASK-672 (frontend) — buyer-facing supplier-metrics detail page + trend charts + deep-link tiles

**Status:** done · **Agent:** frontend-developer · Builds on TASK-671. Not pushed.
Log: `.claude/logs/tasks/672_2026-09-01_supplier-metrics-detail-page_frontend-developer.md`

New route `marketplace/[id]/metrics` — 7 anchored sections (`rating`/`delivery`/`accuracy`/`quality`/
`response`/`cancellation`/`coverage`), each: current value + keyed explanation + Recharts trend chart
from new `GET .../metrics-history` (`useSupplierMetricsHistory`, key `["marketplace","metrics-history",id,days]`).
New `SupplierMetricTrendChart` (LossesTrendChart clone, gaps on null, `<2` pts → empty state) +
`DeliveryRegionComparison` (declared-vs-actual per measured region). `SupplierMetrics` profile tiles are
now `<Link>`s deep-linking to `/metrics#<anchor>` (+ "Детальніше про показники →"); inline region toggle
removed. New TS `SupplierMetricsHistoryPoint`. i18n: `Dashboard.marketplace.metricsPage.*` ×16 (uk+en),
section titles reuse `metrics.*`. tsc/lint clean, vitest 59/59, uk==en parity 4652, `next build` OK
(route 8.94 kB). Live-verified against dev stack (tiles→section scroll, back link, all sections,
by-region declared/actual, quality empty-state); seed data reverted.

## TASK-671 (backend) — nightly metric snapshots + GET suppliers/{id}/metrics-history

**Status:** done · **Agent:** backend-developer · Builds on TASK-670.
Log: `.claude/logs/tasks/671_2026-09-01_metrics-history-worker-and-endpoint_backend-developer.md`

Worker `supplier-metrics-recompute.job.ts`: after the `supplier_metrics` upsert, ALSO writes one
append-only row/supplier/day into `supplier_metrics_snapshots` — FULL copy incl. Rating +
QualityScore (read back from the just-upserted `supplier_metrics` row; write-boundary rule applies
only to the live shared row, snapshot table is distinct + append-only + UNIQUE (SupplierId,
SnapshotDate) → no clobber). Idempotent, same `SET app.role='worker'` scope. `index.ts` unchanged.
New `GET /api/marketplace/suppliers/{id}/metrics-history?days=90` — `[Authorize]` +
`[RequireModule("marketplace")]`, `days` clamped `[7,365]`, oldest→newest, 404 on
missing/unpublished. Repo `GetMetricsHistoryAsync` inside `IProviderRlsOverride` / `AsNoTracking`
(cross-tenant read), pure LINQ — KI-036 rule intact. DTO `SupplierMetricsHistoryPointDto`.
Build 0 err; `dotnet test` **2174/2174** (0 skipped); Marketplace filter 325/325. Dev-DB dry-run:
row lands for CURRENT_DATE, 2nd run updates not duplicates. Not pushed. openapi.json not regen'd
(already months stale).

## TASK-670 (DB) — `supplier_metrics_snapshots` table for supplier-metric history

**Status:** done · **Agent:** database-engineer · Migration `20260901193439_AddSupplierMetricsHistory`.
Log: `.claude/logs/tasks/670_2026-09-01_supplier-metrics-snapshots-table_database-engineer.md`

New append-only table — nightly worker upserts one row per (supplier, day) from `supplier_metrics`
aggregates; feeds a planned buyer-facing metric trend-chart page. UNIQUE `(SupplierId, SnapshotDate)`
(also serves the `ORDER BY SnapshotDate DESC` history query via backward scan — no dedicated DESC
index), leading `(TenantId)` index. Full RLS triad (`tenant_isolation` NULLIF-guard / `provider_bypass`
provider+provider_admin / `worker_bypass`) added explicitly in the migration, verbatim from live
`supplier_metrics`, no `WITH CHECK`. Build 0 err; `dotnet test` 2158/2158; RLS-audit filter 61/61
(0 skipped) — `AllForceRlsTables_...` passes with the new table. Dev DB migrated (applied via
`shelfguard_app_dev` role, `Down()` round-trip verified). Not pushed.

## TASK-669 (QA) — verification + regression: structured delivery fields + primary category

**Status:** done · **Agent:** qa-tester · **Verdict: SHIP.** Covers TASK-665..668 (HEAD `c2e33f62`).
Log: `.claude/logs/tasks/669_2026-09-01_coverage-fields-qa_qa-tester.md`

No blocking bugs. Automated: build 0 err / 1 known warn; `dotnet test` **2158/2158**; frontend
tsc+lint clean, vitest **59/59**; uk⇄en parity 4636=4636; mobile+worker tsc clean; EF snapshot
consistent (no schema change). E2E (backend from source `:5050` on dev DB, frontend `:3001`,
browser): all 9 steps PASS — structured coverage save/reload (DB JSON has the 4 fields, no `terms`),
buyer display panel + `/coverage` `buyerRegionEntry`, contract PDF §5 «РЕГІОНИ ТА УМОВИ ДОСТАВКИ»
line "1–3 дні, від 5000 грн, <note>" in correct Ukrainian, provider create-tenant supplier-category
(valid 201 / bogus 400 / non-supplier ignored) + wizard gating, category read-only on both profile
PUT paths, `PUT .../supplier-category` (204 / 400 bogus / 204 null-clear / 400 non-supplier / 404),
collapsible sections toggle, legacy `terms`→`note` self-heal + rewrite-without-`terms` on save,
mobile static. Regression: region filter still matches served entries with extra keys, non-registry
category (`dairy`) doesn't crash profile/list/PDF (degrades to `categoryNone`), `DeliveryCoverageBackfill
--apply` idempotent, existing marketplace flows 200.

**Non-blocking:** LOW — uk day-plural grammar fixed strings ("до 1 днів"); LOW — web read-only
category shows `categoryNone` (not raw string) for legacy non-registry keys like `dairy`/`test`.
Dev-DB left with structured coverage on `alpha@supplier.local` + 3 `QA669 *` test tenants.

## TASK-669 (docs) — document structured delivery-coverage fields + primary supplier category

**Status:** done · **Agent:** documentation-writer · Docs only.
Log: `.claude/logs/tasks/669_2026-09-01_coverage-fields-docs_documentation-writer.md`

Consolidated documentation (TASK-665..668). ADR-036 amendment: (1) structured per-region delivery
entry fields (`deliveryDaysMin`, `deliveryDaysMax`, `minOrderAmount`, per-region `note`) replacing
single `terms: string` — JSON camelCase, no migration, legacy self-heal on read, no write-back;
`SupplierAgreementService.FormatDeliveryTerms` flattens back to contract PDF single line. (2) One
primary supplier category (0–1), set at creation via `CreateTenantRequest.supplierCategory` (both
provider/admin paths), validated for `businessType=="supplier"`, read-only after — rationale:
immutable identity. New `PUT /api/provider/tenants/{id}/supplier-category` (ProviderOnly) to correct
post-creation. Cleanup tool + dev DB run (1 profile `[auto_parts,medical,food]`→`[auto_parts]`).
Files: `decisions.md` (ADR-036 amendment), `domain-model.md` (SupplierProfile shape),
`api-contracts.md` (DTOs/endpoint), `known-issues.md` (KI-041/042). Dates bumped to 2026-09-01.

## TASK-667 — read-only single category + collapsible profile sections + supplier-category at tenant creation

**Status:** done (committed to main) · **Agent:** frontend-developer · Frontend only.
Consumes TASK-665 (backend, `de8f1632`) + TASK-666 (`f5036244`).
Log: `.claude/logs/tasks/667_2026-09-01_profile-forms-and-category-selector_frontend-developer.md`

New `frontend/components/ui/CollapsibleSection.tsx` — generic `{ title, defaultOpen=true,
children }` dark panel (lucide chevron header, `aria-expanded`). Both supplier profile forms
(`CabinetProfileForm`, `SupplierProfileForm`): categories checklist / `TagInput` → read-only
single-category line (`itemCategories.find(c => c.key === (profile.categories ?? [])[0])?.labelUa`
or `categoryNone`); `categories` dropped from the update payload (`SupplierProfileUpdateRequest.
categories` now optional). Four `CollapsibleSection`s (all `defaultOpen`): general (region+website),
category, delivery coverage, schedule+payment; save/publish/plan controls stay outside. Region
field relabelled `regionLabel` → "Регіон відправлення" / "Origin region" + new `regionHint`.
Provider `CreateTenantWizard` step 2 + admin `CreateTenantModal`: single-select "Категорія товарів"
(from `useItemCategories()`, 4 opts) shown only for `businessType==="supplier"`, required to
proceed / submit, threaded as `supplierCategory` into the create payload only for suppliers.
`provider/types.ts` + `admin/types.ts` `CreateTenantRequest += supplierCategory?: string` (api/hook
layers pass through untouched). i18n both locales: profileForm `sectionGeneralLabel`/
`sectionScheduleLabel`/`regionHint`/`categoryReadonlyLabel`/`categoryNone` (×2 subtrees),
wizard + modal `supplierCategoryLabel`/`supplierCategoryHint` (+ modal `supplierCategoryPlaceholder`).
tsc/lint/vitest(59)/uk-en parity(0 drift) all clean.

## TASK-666 — structured per-region delivery fields in coverage editor + display panels

**Status:** done (committed to main) · **Agent:** frontend-developer · Frontend only. Consumes
TASK-665 (backend, de8f1632); sibling of TASK-668 (mobile).
Log: `.claude/logs/tasks/666_2026-09-01_coverage-editor-structured-fields_frontend-developer.md`

`DeliveryCoverageEntry` (geo/types) `{regionCode,terms}` → `{regionCode,deliveryDaysMin,
deliveryDaysMax,minOrderAmount,note}`. `SupplierCoverageForBuyer.buyerRegionTerms` →
`buyerRegionEntry: DeliveryCoverageEntry | null`. New `features/geo/lib/formatDeliveryTerms.ts`
(+9 vitest cases) builds `"1–3 дні · від 5000 грн"` from the structured fields; per-region `note`
rendered separately by callers. `DeliveryCoverageEditor` — 3 number inputs + note input per served
region, 3 sub-sections behind local collapsible headers (expanded by default; `CollapsibleSection`
= TASK-667). `SupplierCoveragePanel` + `CooperationCoveragePanel` render the formatted string
(`termsByAgreement` fallback) + note line. New i18n `Dashboard.geo.deliveryTerms.*` (5 keys) +
`coverageEditor.*` (5 labels); dropped orphan `servedTermsPlaceholder`. tsc/lint/vitest(59)/uk-en
parity(4621) all clean. Interactive verify blocked — no local backend (port 5000 taken by an
unrelated container); staging predates TASK-665.

## TASK-668 — mobile coverage display for the new structured delivery fields

**Status:** done (committed to main) · **Agent:** mobile-developer · Mobile only. Follows TASK-665
(backend, commit de8f1632). Read-only display — no editing on mobile.
Log: `.claude/logs/tasks/668_2026-09-01_mobile-coverage-structured-fields_mobile-developer.md`

`mobile/features/geo/types.ts` (where `DeliveryCoverageEntry` is actually defined — `marketplace/types.ts`
only re-imports it): served-entry `terms: string | null` → `(deliveryDaysMin, deliveryDaysMax,
minOrderAmount, note)`, all `number | null` / `string | null`. `mobile/app/(app)/marketplace/[id].tsx`:
new local `formatDeliveryTerms(entry)` helper flattens the structured fields into one Ukrainian line
(days range "1–3 дні" / "від 2 днів" / "до 3 днів" / "N дн." · amount "від 5000 грн"; joined with " · ";
"за домовленістю" when empty); per-region `entry.note` rendered on its own muted italic line below.
`npx tsc --noEmit` clean. Concurrent web sibling task in progress (uncommitted `frontend/features/geo|marketplace/…`
+ new `frontend/features/geo/lib/formatDeliveryTerms.ts`) — not touched.

## TASK-665 — structured per-region delivery fields + single primary supplier category

**Status:** done (committed to main) · **Agent:** backend-developer · Backend only. Modifies the
shipped-but-not-deployed delivery-coverage feature (TASK-648..664, ADR-036). No DB migration.
Log: `.claude/logs/tasks/665_2026-09-01_coverage-fields-and-primary-category_backend-developer.md`

**Change A** — `DeliveryCoverageEntryDto` `(RegionCode, string? Terms)` → `(RegionCode, int? DeliveryDaysMin,
int? DeliveryDaysMax, decimal? MinOrderAmount, string? Note)`. `SupplierCoverageForBuyerDto.BuyerRegionTerms`
→ `DeliveryCoverageEntryDto? BuyerRegionEntry`. `DeliveryCoverageJson`: legacy `terms` self-heals into `note`
on read, never written back; `Normalize` swaps reversed day pairs; `Validate` adds 0..365 days /
non-negative min-order. `SupplierAgreementService.FormatDeliveryTerms` flattens the structured fields into
the contract PDF's existing single `Terms` line — `ContractPdfGenerator` untouched. Repo region filter
(`@>` subset match) unaffected — verified + test-seeded.

**Change B** — a supplier profile now holds 0 or 1 category, chosen at tenant creation, read-only after.
`CreateOwnerManaged(…, string? primaryCategory)`. `ProviderService`/`TenantAdminService` `CreateTenantRequest`
+= `string? SupplierCategory` (validated only for `businessType=="supplier"`). Profile-update endpoints stop
writing `Categories` (kept on wire, ignored). New `PUT /api/provider/tenants/{id}/supplier-category`
(+ `ProviderService.SetSupplierCategoryAsync`) to fix existing suppliers. One-shot cleanup added to
`ShelfGuard.Tools.DeliveryCoverageBackfill` — **ran on dev DB**: 1 profile `[auto_parts,medical,food]`→`[auto_parts]`.

Build 0 err; `dotnet test` **2158/2158** (baseline 2134 + 24). openapi.json regen still pending (KI-040).

## TASK-664 (BUG-1) — fix: cooperation coverage panel "region not declared" vs "region unknown"

**Status:** done (committed to main) · **Agent:** frontend-developer · Fixes BUG-1 from TASK-663 QA.
Frontend only, small fix.
Log: `.claude/logs/tasks/664_2026-08-31_fix-cooperation-coverage-region-unknown_frontend-developer.md`

`CooperationCoveragePanel.tsx`: the `buyerRegionStatus === "unknown"` branch now splits on
`buyerRegionCode?.trim()`. Resolved-but-undeclared region (case b) → one neutral advisory line
`t("regionNotDeclared", {region})`, no `<RegionSelect>`, full coverage summary still shown, submit
never blocked. Genuinely-unresolved region (case a) → unchanged `regionUnknown` copy +
`<RegionSelect>` override. New i18n key
`Dashboard.marketplace.cooperationRequestModal.coverage.regionNotDeclared` ({region} param) in
uk + en. No backend/DTO change. tsc + lint clean, vitest 50/50, uk/en parity 4612=4612.
Browser-verified case (a)/(b)/served on dev :3007 in uk + en.

## TASK-663 (T16) — e2e + regression QA: supplier delivery-coverage / performance-metrics

**Status:** done · **Agent:** qa-tester · Marketplace supplier-performance plan
(`eventual-whistling-rabbit.md`), T16 of T1–T16. Covers TASK-648..662 (merged to `main`, HEAD `f11425cd`).
Log: `.claude/logs/tasks/663_2026-08-31_coverage-feature-qa_qa-tester.md`

**Verdict: SHIP WITH NOTES.** Automated: build 0 err / 1 known warn; `dotnet test` **2134/2134**
(RLS audit + 185 coverage-feature tests green); frontend tsc/lint/vitest clean, i18n parity 4611=4611;
mobile tsc + worker build clean; EF snapshot consistent. E2E: all 8 steps PASS — coverage editor
save+reload, region filter (UI+API), always-visible coverage panel + per-region drill-down, cooperation
modal panel (advisory), contract PDF §5 «РЕГІОНИ ТА УМОВИ ДОСТАВКИ» / §6 signatures in correct Ukrainian,
order `DestinationRegionCode=UA-30` snapshot, real BullMQ worker run (**`Rating` + `UpdatedAt` UNCHANGED**,
`AvgDeliveryDays=3.00`, `DeliveryByRegion` UA-30 n=1), mobile static review + KI-037 fix confirmed.
Regression: legacy `Region ILIKE` fallback + `DeliveryCoverageBackfill --apply` → structured `served` match,
null-coverage profile/PDF don't crash, unfiltered supplier set unchanged (3). Dev data seeded then cleaned.

**BUG-1 (LOW, non-blocking):** `CooperationCoveragePanel.tsx` shows "Не вдалося визначити ваш регіон"
even when the buyer's region IS resolved but the supplier just didn't declare it (`BuyerRegionStatus`
enum can't distinguish "region unknown" from "coverage for known region undeclared"). Advisory panel,
never blocks submit. Fix sketch in the log.

## TASK-659 (T12) — i18n sweep for the supplier-coverage feature

**Status:** done (committed to main) · **Agent:** frontend-developer · Marketplace supplier-performance
plan (`eventual-whistling-rabbit.md`), T12 of T1–T16. Depends on TASK-654..658, all on main. Frontend only.
Log: `.claude/logs/tasks/659_2026-08-31_i18n-sweep-coverage_frontend-developer.md`

Прибрано хардкод українських рядків зі спільних `features/geo` компонентів
(`DeliveryCoverageEditor`, `RegionMultiSelect`, `RegionSelect`) + мітку регіону в
`LocationFormDialog`. 11 нових ключів у **обох** локалях: `Dashboard.geo.coverageEditor.*`
(5), `Dashboard.geo.regionMultiSelect.{loading,emptyHint}`,
`Dashboard.geo.regionSelect.{allPlaceholder,choosePlaceholder}`,
`Dashboard.locations.form.{regionLabel,regionPlaceholder}`. Top-level `Geo` неможливий —
`DashboardIntlProvider` віддає лише `Common`+`Dashboard`, тож `Dashboard.geo.*` (узгоджено
з планом). `RegionSelect` `placeholder` prop-override збережено, змінено лише fallback.
Sweep: понад список у брифі нічого не знайдено. tsc/lint/vitest clean; uk/en parity 4611==4611.

## TASK-662 (T15) — docs for the supplier delivery-coverage / performance-metrics feature

**Status:** done (committed to main) · **Agent:** documentation-writer · Marketplace supplier-performance
plan (`eventual-whistling-rabbit.md`), T15 of T1–T16. Depends on TASK-648..661, all on main. Docs only.
Log: `.claude/logs/tasks/662_2026-08-31_coverage-feature-docs_documentation-writer.md`

New **ADR-036** in `decisions.md` (region taxonomy = app-side `UkraineRegions` constant not a DB
table; `MarketplaceOrder.DestinationRegionCode` point-in-time snapshot per ADR-033; delivery
coverage deliberately NOT premium-gated; `supplier-metrics-recompute` worker write-boundary — never
`Rating`/`QualityScore`, no `xmin`, mirrors ADR-034 D4). `domain-model.md`: Ukraine Region Registry
+ `SupplierProfile.DeliveryCoverage` (supersedes `[Obsolete]` `DeliveryRegions`) + `SupplierMetrics`
now populated (4 new cols) + `Location.RegionCode`/`MarketplaceOrder.DestinationRegionCode`.
`database-schema.md`: new `## TASK-649 — Supplier performance data` (4 col groups, 2 indexes,
"no new tables / no RLS change" note). `api-contracts.md`: new marketplace section — `GET /api/geo/regions`,
`GET /api/marketplace/suppliers/{id}/coverage`, `region`→`regionCode` rename + coverage-match
semantics, `SupplierProfileDto`/`SupplierMetricsDto`/`*ProfileUpdateDto` field additions, contract
PDF §5/§6. `known-issues.md`: KI-038 (metrics measurement limitations), KI-039 (backfill match rate),
KI-040 (openapi.json not regenerated — pending chore). All 5 doc headers date-bumped to 2026-08-31.

## TASK-657 (T10) — delivery-coverage panel in `CooperationRequestModal` (frontend)

**Status:** done (committed to main) · **Agent:** frontend-developer · Marketplace supplier-performance
plan (`eventual-whistling-rabbit.md`), T10 of T1–T16. Depends on T4 (TASK-651), T7 (TASK-654) — both
on main. Frontend only.
Log: `.claude/logs/tasks/657_2026-08-31_cooperation-modal-coverage_frontend-developer.md`

New `CooperationCoveragePanel.tsx` rendered at the top of the cooperation-request modal (above the
message textarea). `marketplace/types.ts`: `BuyerRegionStatus` + `SupplierCoverageForBuyer` (matches
backend `SupplierCoverageForBuyerDto`). `marketplace-api.ts`: `getSupplierCoverageForBuyer(id,
buyerRegionCode?)` → `GET /api/marketplace/suppliers/{id}/coverage`. `useMarketplace.ts`:
`useSupplierCoverageForBuyer(supplierId|null, buyerRegionCode?|null)`, key
`["marketplace","supplier-coverage",id,code]`, `enabled: !!supplierId`.
Panel: served → green line + terms + measured-days (when non-null); not_served → amber line;
unknown → neutral line + `<RegionSelect>` (local `useState` → fed back as `?buyerRegionCode=`
override, re-resolves the panel). Compact full-coverage summary below. Advisory only — never blocks
submit. i18n: new `cooperationRequestModal.coverage.*` sub-object (10 keys) in uk+en, parity
4600=4600. tsc + lint clean, vitest 50/50. Browser-verified all 3 states (served/not_served/unknown
+ override) on dev :3007, uk + en.

## TASK-655 (T8) — profile editors + marketplace region filter use the structured region taxonomy (frontend)

**Status:** done (committed to main) · **Agent:** frontend-developer · Marketplace supplier-performance
plan (`eventual-whistling-rabbit.md`), T8 of T1–T16. Depends on T3 (TASK-650), T4 (TASK-651),
T7 (TASK-654) — all on main. Frontend only.
Log: `.claude/logs/tasks/655_2026-08-31_profile-editors-region-filter_frontend-developer.md`

`marketplace/types.ts`: `MarketplaceSearchRequest.region`→`regionCode`, `MarketplaceFilters.region`
→`regionCode`, `SupplierProfileUpdateRequest` drops `deliveryRegions` / adds `deliveryCoverage?`.
`supplier-cabinet/types.ts`: `CabinetProfile` + `CabinetProfileUpdateRequest` gain `deliveryCoverage?`.
`marketplace-api.ts` / `useMarketplace.ts` / `marketplace/page.tsx`: region query param → `regionCode`.
`SupplierFilters.tsx`: free-text region input → `<RegionSelect>` on `filters.regionCode`.
`SupplierProfileForm.tsx` + `CabinetProfileForm.tsx`: region `<input>` → `<RegionSelect>`,
`deliveryRegions` TagInput/comma-input → `<DeliveryCoverageEditor>`.
i18n: 4 new keys (`{marketplace,supplierCabinet}.profileForm.{deliveryCoverageLabel,regionSelectPlaceholder}`)
in uk+en, 3 dead keys removed, `filters.regionPlaceholder` + `profileForm.regionLabel` reworded.
`DeliveryCoverageEditor` needed no label props (internal UA strings — TASK-659's i18n scope).
tsc + lint clean, parity 4590=4590, vitest 50/50. Browser-verified: region filter dropdown +
coverage filtering (served/notServed), both editors render + save (PUT 200, reload persists).

## TASK-661 (T14) — one-shot backfill `DeliveryRegions` → `DeliveryCoverage` codes (backend)

**Status:** done (committed to main) · **Agent:** backend-developer · Marketplace supplier-performance
plan (`eventual-whistling-rabbit.md`), T14 of T1–T16. Depends on T1 (`UkraineRegions`) + T2
(`DeliveryCoverage` column), both on main.
Log: `.claude/logs/tasks/661_2026-08-31_delivery-regions-backfill_backend-developer.md`

New standalone console tool `backend/ShelfGuard.Tools.DeliveryCoverageBackfill` (Tools pattern, per
plan) — migrates legacy free-text `supplier_profiles.DeliveryRegions` → structured
`DeliveryCoverage`. Pure transform `DeliveryRegionsBackfill.Build` (Application layer, 9 unit tests):
`UkraineRegions.TryMatchFreeText` match → `served` code (terms null, deduped); no match → `note`
`"Також: …"` so nothing is lost; note-only coverage is still written; `[]`/blank → row untouched.
`DeliveryRegions` kept as audit trail. Idempotent (`DeliveryCoverage IS NULL` guard), one
transaction, **dry-run by default** (`--apply` to persist). RLS: tool asserts
`SET LOCAL app.role = 'provider'` itself (not the contract-locked `IProviderRlsOverride`).
Run in prod: `ConnectionStrings__DefaultConnection=… dotnet run --project
ShelfGuard.Tools.DeliveryCoverageBackfill [-- --apply]` as the non-superuser app role.
Dev-DB run: scanned 2, updated 1 (`ef3a82bb` → note-only `"Також: Odesa"`), skipped 1; 3rd
eligible row already had coverage from a concurrent session (guard skipped it). Latin QA data →
0 code matches, real no-op until prod. Build 0 errors; `dotnet test` 2134/2134. Follow-up: after
prod run, drop the 2 `#pragma CS0618` `DeliveryRegions` reads in `MarketplaceService`/
`SupplierCabinetService` + a later migration dropping the column.

## TASK-658 (T11) — region picker on the Location form (frontend + small backend)

**Status:** done (committed to main) · **Agent:** frontend-developer · Marketplace supplier-performance
plan (`eventual-whistling-rabbit.md`), T11 of T1–T16. Depends on T7 (TASK-654) + TASK-649
(`Location.RegionCode`), both on main.
Log: `.claude/logs/tasks/658_2026-08-31_location-region-picker_frontend-developer.md`

Backend: `RegionCode` (`string?`) added to `LocationDto` / `CreateLocationRequest` /
`UpdateLocationRequest`; `LocationService` create+update validate it via
`UkraineRegions.IsValid` (blank → null, unknown → 400 tuple) and map it through `ToDto`.
Frontend: `features/locations/types.ts` + `api/locations.ts` gain `regionCode`;
`LocationFormDialog.tsx` renders a `<RegionSelect>` ("Область / місто", label hardcoded —
i18n is TASK-659, `messages/*` untouched) bound via `watch`/`setValue`, wired into zod schema,
defaults, both `reset()` branches, `onSubmit` payload; `locations/page.tsx` threads `regionCode`
into create. Build 0/0, tests 90/90, tsc + lint clean. Browser check skipped (needs geo endpoint +
auth running). Follow-up: TASK-659 i18n; pre-existing `ToDto` never maps `LegalEntityId` (separate fix).

## TASK-651 (T4) — coverage-aware region filter + `GET /api/marketplace/suppliers/{id}/coverage` (backend)

**Status:** done (committed to main) · **Agent:** backend-developer · Marketplace supplier-performance
plan (`eventual-whistling-rabbit.md`), T4 of T1–T16. Depends on T3 (TASK-650, on main — same files).
Log: `.claude/logs/tasks/651_2026-08-31_coverage-filter-and-endpoint_backend-developer.md`

`region` → `regionCode` on `GET /api/marketplace/suppliers`, `POST /api/marketplace/search`
(`SupplierSearchDto`), and `IMarketplaceService`/`IMarketplaceRepository`. New
`MarketplaceRepository.ApplyRegionCoverageFilter`: profile matches when `DeliveryCoverage.served`
holds the code and `notServed` does not (`EF.Functions.JsonContains` → server-side jsonb `@>`,
verified via `ToQueryString`, still inside the `IProviderRlsOverride` block — no `GetDbConnection`,
KI-036 rule intact); legacy `DeliveryCoverage IS NULL` profiles fall back to `Region ILIKE` on the
code or its Ukrainian name. `MarketplaceService.NormalizeRegionCode` (via `UkraineRegions.TryMatchFreeText`)
turns a bare code / legacy name into a code, unrecognized → null.
New `GET /api/marketplace/suppliers/{id}/coverage` `[Authorize]`+`[RequireModule("marketplace")]`
→ `MarketplaceService.GetSupplierCoverageForBuyerAsync` → `SupplierCoverageForBuyerDto`
(`buyerRegionStatus` served/not_served/unknown, terms, measured avg days from `DeliveryByRegion`).
Buyer region: valid `?buyerRegionCode=` override, else caller tenant's **oldest active `Location`
with a `RegionCode`** (no first-class primary-location flag exists), else unknown. `MarketplaceService`
now also injects `ILocationRepository`. Build 0 errors; marketplace suite 283/283 (+15 unit, +3
live-Postgres coverage-filter). Follow-ups: frontend `regionCode` wiring → T8/T10, openapi regen → T15.

## TASK-656 (T9) — supplier profile coverage panel + per-region delivery drill-down (frontend)

**Status:** done (committed to main) · **Agent:** frontend-developer · Marketplace supplier-performance
plan (`eventual-whistling-rabbit.md`), T9 of T1–T16. Depends on T3 (TASK-650) + T7 (TASK-654), both on main.
Log: `.claude/logs/tasks/656_2026-08-31_profile-coverage-panel_frontend-developer.md`

New `SupplierCoveragePanel.tsx` (always-visible delivery-coverage panel — served regions + terms,
notServed, note; NOT premium-gated) and `DeliveryByRegionPanel.tsx` (per-region avg-delivery drill-down,
sorted asc, `n=` sample size). `marketplace/[id]/page.tsx`: premium `deliveryRegions` chips → coverage
panel above the metrics section, outside the premium gate. `SupplierMetrics.tsx`: "на основі N замовлень" /
"на основі N звернень" sublabels, "детальніше по регіонах" toggle expanding the drill-down inline,
"недостатньо даних" for null `responseTimeHours`. `types.ts`: reuse geo `DeliveryCoverage`, new
`RegionDeliveryStat`, `SupplierMetricsDto`/`SupplierProfileDto` += read-side fields (appended, minimal
diff vs concurrent TASK-655). New i18n keys in `metrics` + new `coverage` / `deliveryByRegion` namespaces,
both `uk.json` + `en.json` (full parity). `tsc`/`lint`/`vitest` clean; browser-verified both locales
(coverage panel, drill-down toggle, empty states, no missing-key throws). Follow-ups: T10 (coverage in
CooperationRequestModal), T13 (mobile), T15 (docs).

## TASK-660 (T13) — mobile marketplace: delivery coverage + per-region metrics + response-time tile (mobile)

**Status:** done (committed to main) · **Agent:** mobile-developer · Marketplace supplier-performance
plan (`eventual-whistling-rabbit.md`), T13 of T1–T16. Depends on T3 (TASK-650, DTO final form — on main).
Log: `.claude/logs/tasks/660_2026-08-31_mobile-marketplace-coverage_mobile-developer.md`

New read-only `mobile/features/geo/` (`types.ts`, `api.ts` → `GET /geo/regions`, `hooks.ts` with
`useRegions` / `useRegionLabel`, `staleTime: Infinity`). `mobile/features/marketplace/types.ts`:
`SupplierMetrics` += `deliveryByRegion?` / `deliverySampleSize?` / `responseSampleSize?` /
`aggregatesComputedAt?`, new `RegionDeliveryStat`; `SupplierProfile` += `deliveryCoverage?`.
`marketplace/[id].tsx`: info card gets a "Регіони доставки" block (served regions + terms /
"за домовленістю", muted "Не доставляє: …", note) shown only when `deliveryCoverage` present;
metrics card gains a **"Час відповіді"** tile (`responseTimeHours` + " год.", "недостатньо даних"
when null) and a collapsible per-region delivery list ("Детальніше по регіонах"). **Bug fix
(KI-037):** `orderAccuracy` / `qualityScore` tiles rendered 0–1 fractions as `Math.round(x)%`
(0.87 → "0%") → now `Math.round(x*100)%`; `qualityScore` always null → renders "—". Каталог /
Відгуки tabs unchanged. tsc clean; lint clean (1 pre-existing unused-import warning); jest green.
## TASK-652 (T5) — delivery-coverage section in the cooperation-contract PDF (backend)

**Status:** review (worktree branch `worktree-agent-ae1eafb63d6be4bf5`, not merged) · **Agent:** backend-developer ·
Marketplace supplier-performance plan (`eventual-whistling-rabbit.md`), T5 of T1–T16. Depends on T1 (`UkraineRegions`)
+ T3 (`DeliveryCoverageJson`, `SupplierProfile.DeliveryCoverage`) — both merged to main.
Log: `.claude/logs/tasks/652_2026-08-31_contract-pdf-delivery-coverage_backend-developer.md`

`ContractPdfData` += 3 trailing optional params (`DeliveryCoverageServed: IReadOnlyList<ContractDeliveryRegion>?`,
`DeliveryCoverageNotServed: IReadOnlyList<string>?` — resolved region NAMES, `DeliveryCoverageNote: string?`) + new
`ContractDeliveryRegion(string RegionName, string? Terms)` record. `ContractPdfGenerator` renders new section
**«5. РЕГІОНИ ТА УМОВИ ДОСТАВКИ»** (5.1 lead + 2-col served table [region | умови, "за домовленістю" when no terms] +
5.2 not-served line + 5.3 note) immediately before signatures, which are **renumbered to «6. ПІДПИСИ СТОРІН»**; section
renders only when `DeliveryCoverageServed` is non-empty (same optional-block style as client requisites). Generator stays
IO-free / no `UkraineRegions` dependency. `SupplierAgreementService.GenerateAndStoreContractAsync` loads the supplier's
own `SupplierProfile.DeliveryCoverage` via existing `IMarketplaceRepository.GetOwnProfileAsync` (plain tenant RLS — the
supplier is the approving party), `DeliveryCoverageJson.Parse` + resolves each code → `UkraineRegions.Find(code)?.NameUa ?? code`,
passes ready strings in. Null/empty coverage → nulls (section absent). No existing PDF-test assertion changes needed
(existing tests use a mocked generator / don't grep section numbers). +3 `ContractPdfGeneratorTests` (coverage renders &
grows PDF; served-only vs +extras; null coverage keeps a valid PDF) +2 `SupplierAgreementServiceTests` (code→name resolve
into `ContractPdfData`; no-profile → null coverage). Build 0 errors; full suite 2109/2109.

## TASK-650 (T3) — coverage DTOs + `DeliveryCoverageJson` + profile services + order region snapshot (backend)

**Status:** done (committed to main) · **Agent:** backend-developer · Marketplace supplier-performance
plan (`eventual-whistling-rabbit.md`), T3 of T1–T16. Depends on T1/T2 (both on main).
Log: `.claude/logs/tasks/650_2026-08-31_coverage-dtos-profile-services_backend-developer.md`

New `DeliveryCoverageDto` / `DeliveryCoverageEntryDto` / `RegionDeliveryStatDto` /
`SupplierCoverageForBuyerDto` in `MarketplaceDtos.cs`; `SupplierProfileDto` += `DeliveryCoverage`
(**not** premium-gated), `SupplierMetricsDto` += 4 worker-aggregate fields (all appended-optional),
`SupplierProfileUpdateDto`/`CabinetProfileUpdateDto` += `DeliveryCoverage` (patch), `DeliveryRegions`
now ignored on input. New `DeliveryCoverageJson` helper (Parse/Serialize/Validate, camelCase, reuses
`UkraineRegions.Validate`). `MarketplaceService` + `SupplierCabinetService`: coverage read
unconditionally, validate+serialize on write, **stopped writing `DeliveryRegions`** (4 `CS0618`
warnings gone; 2 legacy reads pragma-suppressed until T14 backfill). `MarketplaceOrderService.CreateOrderAsync`
snapshots `order.DestinationRegionCode` from the destination `Location` (injected `ILocationRepository`).
`SupplierCabinetController.UpdateProfile` now returns 400 (not 404) for validation errors. Build 0
errors; marketplace/cabinet/order/coverage suites 268/268 (+19 new). Follow-ups: T4 (coverage endpoint +
repo filter), T5 (contract PDF), T6 (worker), T14 (backfill), openapi.json regen → T15.

## TASK-649 (T2) — `AddSupplierPerformanceData` migration + entity/DbContext (database)

**Status:** done (merged to main) ·
**Agent:** database-engineer · Part of the marketplace supplier-performance plan
(`eventual-whistling-rabbit.md`), T2 of T1–T16.
Log: `.claude/logs/tasks/649_2026-08-31_supplier-performance-data-migration_database-engineer.md`

Pure additive DDL — **no new tables, no RLS policy changes** (the 4 target tables already carry
`tenant_isolation`/`provider_bypass`/`worker_bypass`; new columns inherit them).
`20260831090731_AddSupplierPerformanceData`:
`locations.RegionCode varchar(20)`, `marketplace_orders.DestinationRegionCode varchar(20)`
(snapshot), `supplier_profiles.DeliveryCoverage jsonb` (supersedes `DeliveryRegions`, which is
now `[Obsolete]` but kept), `supplier_metrics` += `DeliveryByRegion jsonb` /
`DeliverySampleSize int` / `ResponseSampleSize int` / `AggregatesComputedAt timestamptz`.
Indexes: EF composite `(SessionId, SenderTenantId, CreatedAt)` on `supplier_chat_messages`;
hand-written partial `ix_marketplace_orders_metrics ("SupplierTenantId","DeliveredAt") WHERE
"Status" = 'delivered'`. Region-code length is `varchar(20)`, a deliberate deviation from the
plan's `varchar(12)` (too short for `UA-XX-LONGTRANSLIT` city codes). Build 0 errors (4 expected
`CS0618` warnings in T3-owned files); RLS audit 61/61; marketplace/supplier suites 250/250.
Applied + `Down()` round-tripped on dev DB via `shelfguard_app_dev`; `pg_policies` identical
before/after. Follow-ups: T3/T4 (service/DTO/repo), T6 (worker), T15 (docs).

## TASK-654 — Shared `frontend/features/geo/` components (T7, frontend)

**Status:** done · **Agent:** frontend-developer · Part of the "supplier delivery-coverage / metrics"
plan (`eventual-whistling-rabbit`). Depends on T1 (`GET /api/geo/regions`, backend, concurrent).
Log: `.claude/logs/tasks/654_2026-08-31_geo-shared-components_frontend-developer.md`

New `frontend/features/geo/`: `types.ts` (`Region`, `DeliveryCoverageEntry`, `DeliveryCoverage`),
`api/geo-api.ts` (`geoApi.getRegions` via `@/lib/api`), `hooks/useRegions.ts` (React Query,
`["geo","regions"]`, `staleTime: Infinity` + `useRegionLabel()`), `lib/regionLabel.ts`
(`regionLabel` + `groupRegions`), and 3 controlled dark-theme components: `RegionSelect`
(single `<select>` with `<optgroup>` per oblast), `RegionMultiSelect` (grouped checkbox
checklist, `disabledCodes`), `DeliveryCoverageEditor` (served + per-region terms / notServed /
note, emits fully-formed `DeliveryCoverage`). No region list hard-coded — all from `useRegions()`.
`tsc --noEmit` + `npm run lint` clean. Not wired into any page yet (T8–T12); no i18n yet.

## TASK-648 — Geo: UkraineRegions registry + `GET /api/geo/regions` (backend)

**Status:** done (2026-08-31) · **Agent:** backend-developer · **Depends:** — · T1 of plan
`eventual-whistling-rabbit.md` (supplier delivery-coverage feature).
Log: `.claude/logs/tasks/648_2026-08-31_geo-regions-registry_backend-developer.md`

New `ShelfGuard.Domain/Constants/UkraineRegions.cs` (mirrors `SupplierItemCategories.cs`):
`RegionDefinition(Code, NameUa, Kind, ParentCode)`; `All` = 27 ISO 3166-2:UA oblast-level
units + 24 major cities (`Kind="city"`, `Code="{oblast}-{TRANSLIT}"`); helpers `Find`,
`IsValid`, `Validate`, `TryMatchFreeText` (for the T14 backfill). New `Features/Geo/`
(`RegionDto`, `IGeoService`/`GeoService`, no DB) + thin `GeoController`
(`GET /api/geo/regions`, `[AllowAnonymous]` per item-categories precedent) + DI. Tests:
`UkraineRegionsTests`, `GeoServiceTests`, `GeoControllerTests` — 41/41 green; `dotnet build`
0 errors. No migration / RLS / marketplace files touched.
Downstream contract: `RegionDto { code, nameUa, kind: "oblast"|"city", parentCode: string|null }`.
## TASK-653 — Worker: nightly `supplier-metrics-recompute` job (backend, T6)

**Status:** done · **Agent:** backend-developer · Plan `eventual-whistling-rabbit.md`
§«Worker-задача». Log:
`.claude/logs/tasks/653_2026-08-31_supplier-metrics-recompute-job_backend-developer.md`

Новий `worker/src/jobs/supplier-metrics-recompute.job.ts` + реєстрація в `index.ts`
(черга `supplier-metrics-recompute`, cron `0 2 * * *` — чистий слот перед cleanup 03:00 /
loyalty-tier 04:00). Нарешті наповнює колонки `supplier_metrics`, які з v4 існували наскрізно,
але ніколи не писались. Пише РІВНО `AvgDeliveryDays`, `DeliverySampleSize`, `DeliveryByRegion`,
`ResponseTimeHours`, `ResponseSampleSize`, `CancellationRate`, `OrderAccuracy`,
`AggregatesComputedAt`; **ніколи `Rating`/`QualityScore`/`UpdatedAt`** — `supplier_metrics` без
`xmin`, безпеку дає лише неперетинність колонок із синхронним писачем `Rating`
(`UpsertMetricsRatingAsync`, ADR-035); правило зафіксовано рамкою в шапці job'а.
Доставка — вікно 365 дн. з `DeliveredAt − ShippedAt` + розбивка по `DestinationRegionCode`;
відповідь — медіана годин до першої відповіді в чаті, вікно 180 дн.; cancellation — all-time;
accuracy — лише замовлення з фіналізованим receipt. `sampleSize` завжди реальний 0, не NULL.
`tsc`/`build` чисто; SQL перевірено на dev-БД під `shelfguard_app_dev` + `SET app.role='worker'`
(транзакційний seed із 8 замовлень і ROLLBACK — усі 4 агрегати дали очікувані значення), і
скомпільований job прогнано e2e через BullMQ: 8 постачальників, `Rating` 5.00/5.00/4.00 і
`UpdatedAt` збережені.

## TASK-675 — Каталог: керування штрихкодами (Частина A)

**Status:** done · **Agent:** main-session · План `.claude/plans/1-giggly-catmull.md` (Частина A).
Log: `.claude/logs/tasks/675_2026-09-02_catalog-barcodes_main-session.md`

Без зміни схеми: `Barcodes[0]` = основний ШК (вже фактична конвенція POS/receipts/analytics/mobile).
Новий `features/inventory/components/BarcodeCell.tsx` — у колонці primary + пілюля «+N» + hover/click
портальний поповер зі списком (★ на primary), патерн `ActionMenu.tsx`. `ProductForm` — «☆ Зробити
основним» на кожному не-первинному чипі (перемістити на початок). `ItemService.NormalizeBarcodes()` —
trim/дедуп/порядок у Create+Update. Drawer + detail-page показують весь список. i18n uk+en.
`tsc`+`dotnet build` чисто; e2e в браузері на демо-дані — товар з 3 ШК, make-primary, поповер. Не деплоєно.
Частини B (глобальний каталог категорій) — окремо.

## TASK-676 — B1: глобальний `platform_categories` + міграція

**Status:** done · **Agent:** database-engineer · План `.claude/plans/1-giggly-catmull.md` (Частина B/B1).
Log: `.claude/logs/tasks/676_2026-09-02_platform-categories-migration_database-engineer.md`

Per-tenant `Category` → єдина глобальна provider-керована `PlatformCategory` (`platform_categories`,
**без `TenantId`, без RLS**). AppDbContext/repos/services/seeder/import-tool перепідключено;
`WeatherCoefficient.CategoryId` FK `Cascade`→`SetNull`; `AudienceBuilderRepository.SearchCategoriesAsync`
raw-SQL перероблено на глобальну таблицю; 8 тест-файлів (entity swap + raw-SQL cleanup).
Міграція `20260902114742_AddPlatformCategories` (одна транзакція): create → `NO FORCE RLS` на
categories/items/product_segments/weather_coefficients (міграції йдуть під owner-роллю без
`app.tenant_id` — інакше data-кроки бачать 0 рядків) → seed (union назв по всіх тенантах +
`BusinessTypes` з `tenants.BusinessType`) → drop старих FK (**до** repoint) → repoint по назві →
add нових FK (усі `SET NULL`) → `DROP TABLE categories` → restore `FORCE RLS`. `Down()` — структурне
відновлення + RLS-тріада, дані не відновлюються (irreversible, як `MigrateOrphanSuppliersToTenants`).
Верифікація на dev БД: `items."CategoryId"` non-null **199→199** (0 orphan), `platform_categories`
**86** (= distinct-name старих), row-for-row звірка з бекапом — 0 розбіжностей; `dotnet build` 0/0;
**повний `dotnet test` 2174/2174**; RLS-audit тест green. Backend :5000 зупинено для білду —
**треба перезапустити**. Не закомічено. openapi.json regen — контракт не змінювався (`CategoryDto`
той самий). Далі — B2 (provider CRUD + business_type фільтр).

## TASK-677 — B2: category backend (provider CRUD + business_type фільтр + item-валідація + uncategorized/subtree)

**Status:** done (не закомічено) · **Agent:** backend-developer · План `.claude/plans/1-giggly-catmull.md` (Частина B/B2).
Log: `.claude/logs/tasks/677_2026-09-02_category-backend_backend-developer.md`

`GET /api/categories` тепер фільтрується по `Tenant.BusinessType` (in-memory — jsonb `List<string>`
не транслюється в LINQ-to-SQL; null tenantId=provider → без фільтра; порожній `BusinessTypes` →
видно всім). Новий `ProviderCategoriesController` @ `api/provider/categories` `[ProviderOnly]`
(GET дерево з inactive / POST / PUT / DELETE soft-delete) + `IProviderCategoryService` +
`PlatformCategoryDto`; валідація: name ≤255, business-type allow-list, parent існує + без циклу,
DELETE блокується активними дітьми; `ItemCount` — один grouped-query (provider_bypass → platform-wide).
`ICategoryRepository` розширено (GetAll/GetById/ActiveExists/HasActiveChildren/CountItemsByCategory/
Add/Update/Save). `ItemService` ctor +`ICategoryRepository`, Create+Update валідують `CategoryId`
(`"Category not found or inactive."`). Новий `bool? uncategorized` наскрізь у `IItemService`/
`IItemRepository` `GetAllAsync`+`GetPagedAsync` (перед `ct`, index 4=`ids` збережено) +
`ItemsController`; `ItemRepository.ApplyCategoryFilterAsync` — `uncategorized` → `CategoryId==null`,
інакше set `categoryId` розгортається в усе піддерево (Id/ParentId дерево тягнеться раз, замикається
в пам'яті). Analytics лишено exact-match (drill-down/звіт, не фільтр каталогу) — задокументовано.
2 хендмейд-фейки + NSubstitute call-sites оновлено. `dotnet build` 0 err; **повний `dotnet test`
2200/2200 (+26, 0 regress)**; curl-smoke на dev :5000 — усі кейси (business-type фільтр, provider
CRUD 201/400/403/404, cycle, has-children, uncategorized 18/217, subtree 4→5→4, item-валідація 400).
Backend :5000 перезапущено. openapi.json regen — pending (нові provider-ендпоінти + `uncategorized`).
Далі — B3 (frontend).

## TASK-678 — B3: category frontend (form picker + nested filter + provider management page)

**Status:** review (не закомічено) · **Agent:** frontend-developer · План `.claude/plans/1-giggly-catmull.md` (Частина B/B3).
Log: `.claude/logs/tasks/678_2026-09-02_category-frontend_frontend-developer.md`

Спільні хелпери `features/inventory/lib/categoryTree.ts` (`flattenTree`/`indentLabel`) +
`features/provider/lib/categoryTree.ts` (`buildChildrenMap`/`flattenPlatformTree`/`subtreeIds`).
**D1** `ProductForm`: zod `+categoryId`, новий indented `<select>` (— no category — + дерево)
біля Unit. **D2** `inventory/page.tsx` фільтр: `""`=всі, `__none__`=Без категорії, далі дерево;
query → `category_id`/`uncategorized`; `products.ts`+`useProducts.ts` `+uncategorized?:boolean`.
**D3** нова `/provider/categories` (PROVIDER_ROLES guard) → `<CategoryTreeManager>` (nested tree,
itemCount badge, business-type chips, inactive pill, expand/collapse, add-sub/edit/delete +
confirm-dialog, 400 через `toast.error`) + `<CategoryFormModal>` (`components/ui/Modal`; name,
parent-select мінус self+нащадки, business-types чекбокси з `Dashboard.provider.businessTypes`,
sort, active тільки edit). Нові `providerCategories.ts` API + `useProviderCategories.ts` хуки
(інвалідують `["provider","categories"]` **і** `["categories"]`). `provider/types.ts`
+`PlatformCategoryDto`/`CreateCategoryBody`/`UpdateCategoryBody`. `Sidebar` — пункт у групі `admin`
(`FolderTree`, `PROVIDER_ONLY`+`admin_panel`). i18n uk/en (5333==5333 keys, 0 diff).
`tsc`/`lint` чисто; `next build` exit 0 (`/provider/categories` ○ 7.35 kB). Browser E2E обидва
логіни — усі кейси pass (form→save→Category-колонка, uncategorized 18, subtree-фільтр, indented
dropdown, business-type visibility, provider CRUD + "has sub-categories" 400 + soft-delete pill).
Тестові дані вичищено з dev БД. **⚠️ `next build` побив `.next` запущеного `frontend-dev` —
видалив `.next` + перезапустив (новий serverId).** openapi.json regen — все ще pending з B1/B2.

## QA regression pass — catalog barcodes + platform categories (TASK-675..678)

**Status:** done · **Agent:** qa-tester · **Verdict:** SHIP-WITH-FIXES
Review: `.claude/logs/reviews/2026-09-02_catalog-barcodes-platform-categories-qa.md`

Автоматика вся зелена: `dotnet build` 0 err · **повний `dotnet test` 2200/2200, 0 skipped**
(точний baseline) · has-pending-model-changes none · **міграція Down/Up round-trip на scratch
БД чиста** (Down відновлює `categories` + RLS-тріаду, FK swap-back коректний) · frontend
`tsc`/`lint`/`build` чисто · i18n uk==en 4710/4710, 0 diff · `worker`/`mobile` `tsc` чисто.
RLS: `platform_categories` справді глобальна (no RLS/FORCE), RLS-audit тест проходить з
правильної причини, `items`/`product_segments`/`weather_coefficients` FORCE RLS збережено.
Barcodes (normalize/primary/lookup), provider CRUD 11/11 edge-кейсів, uncategorized+subtree,
audience-builder, mobile by-barcode — усі pass. Browser E2E обидва логіни — pass, 0 console
errors на свіжому сервері (стара `seed`-вкладка мала stale MISSING_MESSAGE — не баг).

**2 баги, обидва на стику `ItemService` category-валідації та provider soft-delete:**
- **BUG-1 (medium):** після provider soft-delete категорії, до якої привʼязані items тенанта,
  будь-яке збереження правки такого item → 400 `"Category not found or inactive."` (форма
  ресабмітить застарілий `categoryId`, а новий `ActiveExistsAsync`-guard його відхиляє).
  Регресія — до цього коміту `UpdateAsync` не валідував `CategoryId`. Фікс: валідувати
  `CategoryId` лише коли він змінився (`id != product.CategoryId`).
- **BUG-2 (low, косметика):** у формі редагування item категорія-`<select>` порожня, якщо
  категорія item прихована від тенанта (retag на інший business-type / soft-delete) — назва
  ніде не показана. Save лінк зберігає (крім кейсу BUG-1). Той самий missing-`<option>`.

Аналітику `by-category` та POS loyalty-exclusion не вдалося E2E (модуль `analytics` не
активний для demo-тенанта; активація заблокована) — code review чистий + покрито тестами в
2200. Уся QA-тестова дата вичищено (БД: 86 platform_categories / 224 items). Не закомічено.

## TASK-679 — Supplier-portal expansion Phase 1 (backend + worker)

**Status:** review (не запушено) · **Agent:** backend-developer · Plan `1-partitioned-book.md` Phase 1
Log: `.claude/logs/tasks/679_2026-09-02_supplier-phase1-backend_backend-developer.md`

Міграція `AddSupplierExpansionFoundations` — 3 nullable колонки, **без RLS-змін**:
`marketplace_orders.CreatedByUserName varchar(255)` (#4), `marketplace_orders.ExpectedDeliveryDate date`
(D5, колонку заводимо зараз), `users.SupplierOrdersLastViewedAt timestamptz` (#3 seen-marker).
**Не застосовано до жодної БД.** Нова `Application/Features/SupplierInventory/` — `ISupplierWarehouseService`
(тонкий врапер над `ILocationService`, форсить `Location.Type="warehouse"`; entity `LocationType` мертвий,
не чіпаємо — рішення в лозі) + `SupplierCabinetWarehousesController` (`api/supplier-cabinet/warehouses`,
gate `supplier_inventory` + `warehouse_management`). #4: `MarketplaceOrderService.CreateOrderAsync` пише
`CreatedByUserName` з клієнт-сесії (`IUserRepository`, новий ctor-параметр), `MarketplaceOrderDto` +=
`createdByUserId`/`createdByUserName`. #3: `CreateOrderAsync` тепер шле `marketplace_order.created` outbox
постачальнику (крос-тенант через `ITenantSessionOverride`). Worker `notification-dispatch.job.ts`:
+`marketplace_order.created` (supplier_admin) + виправлено `marketplace_order.shipped` /
`.delay_reason_added` (мовчки відкидались — не було в матриці; ролі = як у `receipt.created`, тобто
merchandiser). `NotificationService.ValidEventTypes` += 3 типи. `api-contracts.md` оновлено.

`dotnet build -c Release` 0 err (Debug заблоковано — інша сесія тримає running API + DLL-локи);
worker `tsc` чисто; нові unit-тести (SupplierWarehouseService ×8, CreatedByUserName ×2) зелені;
RLS-audit зелений. **5 інтеграційних тестів червоні — виключно через незастосовану міграцію**
(`42703: column "SupplierOrdersLastViewedAt" does not exist` на тест-БД `localhost:5435/crm`) —
до цього коміту зелені; RLS regression pass має спершу зробити `dotnet ef database update` на тест-БД.
Frontend (nav + warehouse UI + «Замовив» колонка + i18n 3 подій) — окремий Phase 1 frontend-агент.


## TASK-680 — Supplier-portal expansion Phase 1 (frontend)

**Status:** review (не запушено) · головна сесія (frontend-агент двічі впав на інфра-помилках:
rate-limit, потім stream-watchdog — нічого не встиг; зроблено вручну)
Log: `.claude/logs/tasks/680_2026-09-03_supplier-phase1-frontend_main-session.md`

Міграцію `AddSupplierExpansionFoundations` **застосовано до dev/test-БД** `localhost:5435/crm`
(docker `crmproductsystems-postgres-1`, ідемпотентний SQL через `psql`) — 5 раніше-червоних
інтеграційних тестів тепер зелені (749 passed у широкому прогоні Marketplace|Notification|Tenant|
Order|Supplier|Location).

Frontend: `ModuleKey` + `TenantModule` унії += `supplier_inventory`/`supplier_workforce`
(2 списки: `features/modules/types.ts`, `features/provider/types.ts`). `Sidebar.tsx` — `useModules`
тепер вантажиться і для `supplier_admin`; новий item-level `moduleKey` гейт (`NavItem.moduleKey`);
пункт `/supplier/warehouses` (gate `supplier_inventory` + `warehouse_management`). Нова фіча
`features/supplier-cabinet/{api,hooks/useSupplierWarehouses,components/WarehousesTab}` + сторінка
`app/(dashboard)/supplier/warehouses/page.tsx` (CRUD складів через тонкий бек-врапер, RegionSelect).
#4: `MarketplaceOrderDto` FE-тип += `createdByUserId`/`createdByUserName`; колонка «Замовив» у
`marketplace/orders/page.tsx` і `CabinetOrdersTab.tsx`. `NotificationEventType` += 3 marketplace-події
(+ `EVENT_TYPE_I18N_KEY`). i18n uk+en (parity 5569==5569): nav, warehousesTab (повний блок), pages,
headerCreatedBy ×2, eventTypes/eventSource ×3, modules.catalog + provider.modules/moduleDescriptions.

`tsc --noEmit` чисто; `next lint` (тільки прееxist warnings у чужому файлі); `next build` OK
(`/supplier/warehouses` 8.03 kB); backend `dotnet build -c Release` чисто.


## TASK-681 — Supplier-portal expansion Phase 2 (backend: складський + партійний облік, D2/D3)

**Status:** review (не запушено) · backend-developer агент
Log: `.claude/logs/tasks/681_2026-09-03_supplier-phase2-inventory_backend-developer.md`

Міграція `20260903063008_AddSupplierInventory` — 4 нові таблиці (`supplier_stock` +
`supplier_stock_movements` + `supplier_stock_receipts` + `supplier_stock_receipt_items`),
паралельні до retail Stock/Receipts (D2/D3 — не реюз). Кожна: RLS-тріада (tenant_isolation
NULLIF-guard + WITH CHECK, provider_bypass, worker_bypass) + FORCE RLS, **без `store_scope`**
(документовано в міграції). `supplier_stock` — xmin rowversion + частковий FEFO-індекс
`(TenantId,WarehouseId,SupplierItemId,ExpiryDate) WHERE Quantity>0`. **Застосовано до dev/test-БД**
`localhost:5435/crm` (ідемпотентний SQL через `psql`, `__EFMigrationsHistory` оновлено).

Backend: `Application/Features/SupplierInventory/` — `ISupplierStockRepository`/`SupplierStockRepository`
(GetPaged FEFO, GetFefoOrdered — дзеркало `StockRepository`; DbUpdateConcurrencyException →
`ConcurrencyConflictException`), `SupplierStockService` (AddBatch + receipt-рух, Adjust xmin-guarded
+ adjust-рух, FefoConsumeAsync — дублікат `StockService.FefoConsumeAsync`, повертає
`SupplierFefoConsumeResult{QuantityConsumed,Shortfall,BatchesConsumed[]}` — нестача НЕ кидається,
для Phase 3), `ISupplierStockReceiptRepository`/`SupplierStockReceiptService` (draft→addLine→finalize;
finalize-гейт: усі рядки мають ExpiryDate + Quantity>0 → 1 `SupplierStock`+1 рух на рядок,
`SourceType="supplier_receipt"`). Реюз `Features/Stock/StockStatus.cs`. DI у обох `DependencyInjection.cs`.

API: новий `SupplierCabinetInventoryController` (`api/supplier-cabinet`, клас-гейт
`[Authorize(SupplierCabinet)] [RequireModule("supplier_inventory")]`, per-action
`HasPermission(User, WarehouseManagement)`): `GET/POST warehouses/{id}/stock`,
`POST stock/{batchId}/adjust`, `GET/POST warehouses/{id}/receipts`, `GET/PUT receipts/{id}`,
`POST/DELETE receipts/{id}/lines[/{lineId}]`, `POST receipts/{id}/finalize`.

`dotnet build -c Release` 0 err; нові тести зелені — `SupplierStockServiceTests` ×13,
`SupplierStockReceiptServiceTests` ×8, `SupplierStockRlsIntegrationTests` ×3 (крос-тенант SELECT
proof на реальному Postgres) + RLS-audit `AllForceRlsTables_...` зелений з 4 новими таблицями;
широкий прогін Stock|Receipt|Location|Marketplace = 540 passed. openapi.json regen — борг (з TASK-670..).

## TASK-682 — Supplier Phase 2 FRONTEND (складський + партійний облік)

**Status:** review (не запушено) · **Agent:** frontend-developer · Plan `1-partitioned-book.md` Phase 2
Log: `.claude/logs/tasks/682_2026-09-03_supplier-phase2-frontend_frontend-developer.md`

Nav `/supplier/inventory` (moduleKey `supplier_inventory` + permission `warehouse_management`, іконка
`Boxes`). Нова сторінка `app/(dashboard)/supplier/inventory/page.tsx` — 2 вкладки «Залишки» /
«Прийоми». `features/supplier-cabinet/`: +10 API-методів у `supplier-cabinet-api.ts`, +6 типів
(`SupplierStock`, `SupplierStockReceipt(+Item)` + 5 request), новий `hooks/useSupplierInventory.ts`
(10 hooks, ключі `["supplier","stock"|"receipts",…]`), компоненти `WarehouseStockTable`
(FEFO-таблиця + adjust-modal, чіпи статусу реюзять `STATUS_COLOR` з `features/shelf`),
`SupplierReceiptForm` (draft→pending rows→persist per line→finalize; N рядків на 1 supplierItemId,
БЕЗ `isRowAdded`-guard; finalize-gate error 400 `{error}` показується as-is),
`SupplierReceiptsList` (таблиця + фільтр статусу + «Новий прийом»). PagedResult для stock —
`@/lib/api-types` (backend Common shape `totalCount/totalPages`), НЕ supplier-cabinet-локальний
(той — marketplace reviews shape `total`). i18n +104 ключів кожна мова, парність 5673==5673.
`tsc` clean · `next lint` clean · `next build` OK.

## TASK-683 — Supplier Phase 3 BACKEND (відвантаження зі списанням партій + передача замовнику, D4)

**Status:** review (не запушено) · **Agent:** backend-developer · Plan `1-partitioned-book.md` Phase 3
Log: `.claude/logs/tasks/683_2026-09-03_supplier-phase3-shipping_backend-developer.md`

Міграція `20260903071530_AddMarketplaceOrderItemBatches` — нова `marketplace_order_item_batches`
зі **split-RLS навпаки** (`tenant_isolation` FOR ALL+WITH CHECK на `SupplierTenantId`, `client_read`
FOR SELECT на `ClientTenantId`, + provider/worker bypass, FORCE RLS) + `marketplace_orders.SourceWarehouseId`
+ `marketplace_order_receipt_items.SourceOrderItemBatchId`. Застосовано до dev/test `:5435/crm`,
`pg_policies`/`relforcerowsecurity` перевірено. **Prod — окремо.**

`MarketplaceOrderService.ShipOrderAsync` — **єдиний** шлях у `shipped`: легасі
`POST orders/{id}/status {status:"shipped"}` тепер делегує в нього з порожнім запитом (нічого не
списує, поведінка байт-у-байт як була). Модуль ON + склад → явні розподіли або авто-FEFO; нестача
= попередження, не помилка. Списання+рухи+партії+статус замовлення — ОДИН атомарний коміт під
supplier-сесією (свідомо НЕ через `FefoConsumeAsync` — вона комітить по-рядково); outbox окремо під
client-override. Нові `POST orders/{id}/ship`, `GET orders/{id}/ship-suggestion?warehouseId=`
(`[RequireModule("supplier_inventory")]` + `warehouse_management`).

`GetOrCreateDraftAsync` — N рядків прийомки на 1 лінію (передзаповнені expiry/batch/qty +
`SourceOrderItemBatchId`), fallback 1/лінію коли партій немає. `ReceiveAsync` без змін.

`dotnet build -c Release` 0 err · повний прогін `dotnet test -c Release` = **2257 passed, 0 failed**.
Новий `MarketplaceOrderItemBatchRlsIntegrationTests` ×5 на реальному Postgres: постачальник пише,
замовник SELECT-ить але отримує **42501** на INSERT і 0 рядків на UPDATE/DELETE, чужий постачальник
і RESET-сесія бачать 0. RLS-audit зелений. Docs: api-contracts.md + амендмент ADR-033.
Mobile handoff: `.claude/logs/handoffs/phase3-mobile-receipt-batches.md`. openapi.json — борг.

## TASK-684 — Supplier Phase 3 FRONTEND (ship modal з партіями + відображення партій, D4)

**Status:** review (не закомічено) · **Agent:** frontend-developer · Plan `1-partitioned-book.md` Phase 3
Log: `.claude/logs/tasks/684_2026-09-03_supplier-phase3-frontend_frontend-developer.md`

`EstimateDeliveryModal.tsx` → перейменовано (`git mv`) у `ShipOrderModal.tsx`, компонент
`ShipOrderModal({ order, onClose })` — сам робить мутації + тости. Модуль OFF (`useModules()` →
`modules.modules.includes("supplier_inventory")`) — старий флоу днів → `useUpdateCabinetOrderStatus`
`{status:"shipped"}`. Модуль ON — `<select>` складу (`useSupplierWarehouses`, дефолт перший
активний) → `useShipSuggestion(orderId, warehouseId)` (enabled поки модалка відкрита) → редаговна
per-line таблиця розподілу (термін/партія/доступно/**кількість**, префіл із suggestion) + чіп
нестачі; дата АБО днів (одне обовʼязкове, деривація дати з днів у підказці) → `useShipOrder`
(POST `/orders/{id}/ship`), warnings з результату → `toast.warning` (не error). Нема активних
складів → fallback: шле `/ship` лише з ETA (легасі-гілка бекенду). `CabinetOrdersTab.tsx` —
імпорт+виклик оновлено (прибрано локальну ship-логіку).

Відображення партій (read-only): `CabinetOrdersTab` + `app/(dashboard)/marketplace/orders/page.tsx`
розгорнутий рядок — під лінією підсписок `item.batches` (термін · партія · к-сть) під міткою
«Партії»; `ShippingDetail` тепер віддає перевагу `order.expectedDeliveryDate` над клієнт-derived
датою (реюз ключа `estimatedDeliveryLabel`). Прийомка замовника (`ReceiptItemsTable`) — N рядків на
лінію рендериться без структурних змін (перевірено).

Типи: `marketplace/types.ts` += `MarketplaceOrderItemBatchDto`, `MarketplaceOrderItemDto.batches`,
`MarketplaceOrderDto.{sourceWarehouseId,expectedDeliveryDate}`, `MarketplaceOrderReceiptItemDto.sourceOrderItemBatchId`;
`supplier-cabinet/types.ts` += `Ship{Allocation,Line,OrderRequest,Suggestion,SuggestionLine,SuggestionAllocation,OrderResult}`.
API +2 (`getShipSuggestion`, `shipOrder`), hooks +2 у `useCabinetCooperation.ts`
(`useShipSuggestion`, `useShipOrder` — invalidate orders + supplier stock).

i18n +23 ключі кожна мова (`Dashboard.supplierCabinet.ordersTab` ship modal +22, `batchesLabel` в
обох ordersTab-неймспейсах). Парність тримається (5513==5513 шляхів, 0 diff).
`tsc --noEmit` clean · `next lint` (touched) clean · `next build` exit 0.
Не чіпав backend / mobile. openapi.json — борг (спільний). НЕ закомічено.

## TASK-685 — Supplier Phase 4: «в дорозі» для автозамовлення + мутабельна дата доставки (D5 / п.2)

**Status:** review (не закомічено) · **Agent:** backend-developer · Plan `1-partitioned-book.md` Phase 4
Log: `.claude/logs/tasks/685_2026-09-03_supplier-phase4-intransit_backend-developer.md`

Фікс подвійного замовлення: відкрите marketplace-замовлення (`new`/`confirmed`/`shipped`) на
магазин-призначення тепер бачить рушій `OrderCalcService`. Новий
`OrderCalcRepository.GetOpenMarketplaceInTransitAsync` (`marketplace_order_items ⋈
marketplace_orders ⋈ items` по `SupplierItemId==SourceSupplierItemId`, `it.TenantId==tenantId`,
`oi.Unit==it.Unit` — рядки з розбіжною одиницею виключено, межа v1). `OrderCalcService` ін'єктить
`ITenantContext`, поєднує `draftReceipts + openMarketplace` в **один** `InTransit`, який формула
вже віднімає. `OrderLineDto` += `InTransitFromMarketplace` (зріз для tooltip). `AiOrderService` —
лише коментар (читає in-transit через `CalculateAsync`).

`MarketplaceOrderService.SetExpectedDeliveryDateAsync` — дзеркало `SetDelayReasonAsync`, гейти
own-supplier + `status==shipped` + дата не в минулому, **повторюване** (без «already set»).
Крос-тенантний outbox `marketplace_order.delivery_rescheduled` під `_tenantSessionOverride`
(client). Ендпоінт `POST /api/supplier-cabinet/orders/{id}/expected-delivery-date` — форма як
`delay-reason` (без `supplier_inventory`/`warehouse_management`). Worker `notification-dispatch.job.ts`
+ dispatch-рядок + іконка. `NotificationService.ValidEventTypes` += подія.

Міграція `20260903112807_AddMarketplaceOrdersReplenishmentIndex` — частковий індекс
`ix_marketplace_orders_open_by_dest` (raw SQL, не в snapshot). **Застосовано до dev DB `:5435`.**
Не до prod.

`dotnet build -c Release` 0 err · `dotnet test --filter "~OrderCalc|~AiOrder|~MarketplaceOrder"`
= **147 passed**; RLS-audit зелений (нема нових таблиць); worker `tsc --noEmit` clean. Нові тести:
`OrderCalcServiceTests`, `OrderCalcRepositoryOpenMarketplaceInTransitTests`, +5 у
`MarketplaceOrderServiceTests`. Docs: `api-contracts.md`. Frontend (типи + редаговна дата +
tooltip + i18n) — окремий агент. openapi.json — борг. НЕ закомічено, `mobile/` не чіпав.

## TASK-686 — Supplier Phase 4 frontend: мутабельна дата доставки + tooltip «в дорозі» + i18n

**Status:** review (не закомічено) · **Agent:** frontend-developer · Plan `1-partitioned-book.md` Phase 4
Log: `.claude/logs/tasks/686_2026-09-03_supplier-phase4-frontend_frontend-developer.md`

Постачальник переносить дату доставки на `shipped`-замовленні: `supplier-cabinet` api
`setOrderExpectedDeliveryDate` + hook `useSetExpectedDeliveryDate` (invalidatе
`["supplier-cabinet","orders"]`) + інлайн `RescheduleDeliveryControl` (date `min=today` + Btn) у
розгорнутому рядку `CabinetOrdersTab` під `ShippingDetail`; 400 → `toast.error(err.message)`.
Контролю замовнику НЕ додано (лише перегляд).

Tooltip розбивки «в дорозі» — `features/orders/components/OrderLinesTable.tsx` колонка `inTransit`
(єдине місце; ai-orders in-transit не показує, DTO не несе). `OrderLine.inTransitFromMarketplace`
додано; `inTransitFromMarketplace===0` → плоске число (без змін для не-marketplace тенантів), інакше
native `title` з двома рядками джерел.

i18n обидві мови: `marketplaceOrderDeliveryRescheduled` (eventTypes + eventSource +
`features/notifications/types.ts` union/map) = «Нова дата доставки» / "Delivery date changed";
`Dashboard.supplierCabinet.ordersTab.reschedule{Label,SaveButton,ToastSaved}`;
`Dashboard.orders.table.inTransitTooltip.{supplierReceipts,marketplaceOrders}`. Парність **5701==5701**.

`tsc --noEmit` clean · `next lint` (7 touched) clean · `next build` EXIT 0 (76/76 pages).
Backend / `mobile/` не чіпав. openapi.json — спільний борг. НЕ закомічено.

## TASK-687 — Supplier Phase 5: графіки працівників постачальника (D6 / п.6)

**Status:** review (не закомічено) · **Agent:** frontend-developer · Plan `1-partitioned-book.md` Phase 5
Log: `.claude/logs/tasks/687_2026-09-03_supplier-phase5-schedules_frontend-developer.md`

Новий тонкий `SupplierCabinetSchedulesController` (`api/supplier-cabinet/schedules`) — pass-through
у спільний `IScheduleService` з tenantId з JWT (як `SupplierCabinetController.InviteStaffAsync →
IUserService`). Клас-гейт `[Authorize(SupplierCabinet)] [RequireModule("supplier_workforce")]`;
кожна мутація — `SupplierPermissionAuthorization.HasPermission(User, WorkforceManagement)` → 403;
GET list/{id}/my-shifts без гейта. **Без міграції, без змін `ScheduleService`** (`work_schedules`/
`schedule_shifts` RLS = tenant_isolation+provider_bypass+worker_bypass, БЕЗ store_scope; сервіс уже
валідує `LocationExistsAsync(locationId, tenantId)` → постачальник чіпляє зміни лише до власних
складів). Пікер виконавця — **новий** `GET /schedules/staff` (гейт `workforce_management`, не
`staff_management`) → `_cabinet.GetStaffAsync`. Тест `SupplierCabinetSchedulesControllerTests`
(10 fact: tenant з JWT, permission-гейт 403 на мутаціях, GET без гейта).

Frontend: `supplierCabinetApi.schedules.*` + `hooks/useSupplierSchedules.ts` (ключі
`["supplier","schedules"|"my-shifts"]`). Компоненти-форки під
`features/supplier-cabinet/components/schedules/` — `SupplierScheduleList/Form/WeekGrid/MyShifts`
(причина форку: retail `WeekGrid` тягне `useUsers` + retail `useSchedules`, `ScheduleForm` тягне
`useLocations`). **Реюз as-is** презентаційних retail `ShiftCard` + `ShiftForm` (props-only) і
retail `features/schedules/types`. Retail `/schedules` не чіпав. Сторінка
`app/(dashboard)/supplier/schedules/page.tsx` — `SUPPLIER_ONLY` + `<ModuleGate
moduleKey="supplier_workforce">`, вкладки «Розклади» (для `workforce_management`) / «Мій розклад»
(усі). Sidebar `buildSupplierNavGroup` += `/supplier/schedules` (`CalendarDays`,
`permission: "workforce_management"`, `moduleKey: "supplier_workforce"`).

i18n обидві мови: nav `supplierCabinet.schedules` = «Графіки»/"Schedules"; `Dashboard.supplierCabinet.schedules.*`
(page + tab + warehouse label); решта — реюз `Dashboard.schedules.*`. Парність **4893==4893**.
`supplier_workforce` module-catalog label вже був (Phase 1).

Backend `dotnet build -c Release` clean · `dotnet test --filter "Schedule|RlsCrossTenant"` 38/38 green.
Frontend `tsc --noEmit` clean · `next lint` (touched) clean · `next build` EXIT 0 (77 routes,
`/supplier/schedules` present). Doc: `.claude/docs/api-contracts.md` оновлено. openapi.json — спільний
борг. НЕ закомічено.
