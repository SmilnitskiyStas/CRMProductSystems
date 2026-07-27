# TASK-420: Backend — Фаза 2 price segments + frequency/reactivation

**Agent:** backend-developer
**Date:** 2026-07-27
**Status:** done — builds clean, full suite green, no blocker. Continuation of a session that hit
its usage limit mid-task (not a code error) — this pass verified the prior agent's partial work
first, then completed the rest.

## Context

Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фази 2-4". Design doc: scratchpad
`phase2-price-segments-design.md`. Competitive source: `docs/uployal/PRICE_SEGMENTS_ANALYSIS.md`.
Depends on TASK-419 (`PriceSegmentSettings` schema, already migrated).

## Verified from the interrupted prior agent (not rewritten)

Read all of these in full before writing anything new; all were correct and complete, matched
the design doc's formulas exactly (§1.3/§1.6/§1.8), and compiled together cleanly:
`PiiMasking.cs` (MaskPhone/MaskEmail moved out of `MarketingAnalyticsService.cs`, byte-for-byte
behavior preserved), `PriceSegmentKey.cs`/`PriceAudienceKey.cs`/`FrequencyAudienceKey.cs`,
`IPriceSegmentAdvisor.cs`, `PriceSegmentClassifier.cs`/`PriceAudienceClassifier.cs`/
`FrequencyAudienceClassifier.cs`, `PriceSegmentCatalog.cs`, `PriceSegmentFilterHash.cs`,
`Dtos/PriceSegmentDtos.cs`. No fixes needed to any of them.

## What this pass built

- `Dtos/FrequencyDtos.cs` — `FrequencyOverviewDto`, `FrequencyAudienceSummaryDto`,
  `FrequencyAudienceTableRequest`/`RowDto`/`TableDto`, `ExportFrequencyAudienceRequest`,
  `FrequencyAudienceRecommendationInputDto`.
- `PriceSegmentRecommendationTemplates.cs` — pure template engine, one method per enum
  (`BuildPriceAudience`/`BuildAllTimeSegment`/`BuildFrequencyAudience`), all 3 sharing
  `PriceSegmentRecommendationDto`. Text sourced from analysis doc §7/§14/§17; `Stable` and
  `Other` (no competitor card for either) got original, conservative copy.
- `PriceSegmentSortKeys.cs` — single allowlist for the 3 tables' `sortBy`, shared by service
  (echoes applied value) and repository (maps to a literal SQL column) so they can't drift apart.
- `IPriceSegmentsRepository.cs` / `PriceSegmentsRepository.cs` — raw-SQL repository (second one
  in the codebase after `MarketingAnalyticsRepository`). 10 methods: per-customer period metrics,
  all-time boundaries, network unit-price, LTV map, all-time KPI/monthly-trend/distribution, and
  the 3 paginated audience/customer tables (real `ORDER BY <allowlisted col> LIMIT/OFFSET` +
  `COUNT(*) OVER()`, never fetch-all-then-paginate-in-C#), plus Settings CRUD.
- `IPriceSegmentsService.cs` / `PriceSegmentsService.cs` — thin orchestration, mirrors
  `MarketingAnalyticsService`'s shape. Overview endpoints classify the full fetched population in
  C# (same as Фаза 1); the 3 table endpoints only map already-paginated/classified SQL rows.
- `Infrastructure/AI/PriceSegmentAdvisor/PriceSegmentAdvisor.cs` — 5th advisor, byte-for-byte
  copy of `MarketingAdvisor`'s Claude-key resolution pattern.
- `Api/Controllers/PriceSegmentsController.cs` + `PriceSegmentSettingsController.cs` — see API
  contract below. Same policy/module gate as `MarketingAnalyticsController`
  (`AppPolicies.MarketingAnalyticsViewOrCapability` + `[RequireModule("marketing_analytics")]`,
  reused literally, no new module key). PII-export gate reuses
  `MarketingAnalyticsAuthorization.CanExportPii` as-is.
- DI: both `Application/DependencyInjection.cs` and `Infrastructure/DependencyInjection.cs`
  updated (`IPriceSegmentsService`, `IPriceSegmentsRepository`, `IPriceSegmentAdvisor`).
- Tests: `PriceSegmentClassifierTests`, `PriceAudienceClassifierTests`,
  `FrequencyAudienceClassifierTests`, `PriceSegmentRecommendationTemplatesTests`,
  `PriceSegmentsServiceTests` (NSubstitute-mocked repo — cohort intersection/union math, previous-
  period date arithmetic, empty-result handling, settings lazy-defaults, PII export/ActivityLog
  audit), `PriceSegmentsRepositoryIntegrationTests` (live Postgres — see below).

## Bug found and fixed via live-DB verification (would NOT have been caught by mocked tests)

`PERCENTILE_CONT(...) WITHIN GROUP (ORDER BY ...)` **always returns `double precision` in
Postgres, never `numeric`, regardless of the input column's type.** Every one of the 15
PERCENTILE_CONT call sites in `PriceSegmentsRepository.cs` originally mapped straight to a C#
`decimal` property — first live run failed 7/10 tests with `InvalidCastException: Reading as
'System.Decimal' is not supported for fields having DataTypeName 'double precision'`. Fixed by
wrapping every call site in `(...)::numeric`. Re-ran live: **10/10 integration tests green**,
including the two riskiest paths — the nullable `int?`/`decimal?` SQL parameters
(`GetAllTimeCustomerTableAsync`'s `segmentOrdinal`, `GetFrequencyAudienceTableAsync`'s
`minSpend`/`maxSpend`/`priceSegmentOrdinal`, all boxed via a new `NullableParam<T>` helper as
`DBNull.Value` rather than a bare C# `null`) and the Sleeping-audience previous-basis
re-orientation. Every audience/segment classification returned by SQL was cross-checked in the
test against the pure C# classifier for the same inputs — confirms the "SQL CASE ladder mirrors
the classifier" contract the doc comments promise, not just plausible-looking numbers.

## Build/test status

`dotnet build` — **0 warnings, 0 errors** (repo convention: clean, not just "succeeds"; fixed 4
new nullable-argument warnings from the `NullableParam` work along the way).
`dotnet test` (full suite) — **1180/1180 green** (was 1109 per TASK-419's baseline; +71: 61 new
unit tests + 10 new live-Postgres integration tests), no regressions.

## API contract for the next frontend agent

Base route `api/marketing-analytics/price-segments`, same auth/module gate as the RFM dashboard.
All GETs take `storeIds` as a repeated query param (omitted/empty = all stores). Every response
DTO field below is camelCase on the wire (confirmed: this API has no custom `JsonNamingPolicy`
configured, ASP.NET Core's `[ApiController]` default applies — same convention already visible in
`frontend/features/marketing-analytics/types.ts` for Фаза 1).

**Comparison mode** (`period=30|60|90` or explicit `from`+`to`, default 30 days):
- `GET .../overview` → `PriceSegmentsOverviewDto`: `periodFrom/To`, `previousPeriodFrom/To`,
  `analyzedCount` (cohort = bought both windows), `currentPeriodBuyerCount`/
  `previousPeriodBuyerCount` (each window's own active-buyer count — THREE different
  denominators, never conflate), `raisedCount`/`declinedCount`/`stableCount`,
  `priceIndexPercent`, `distribution: [{segment, rangeLabelUa, currentCount, previousCount}]`
  (always 7), `audiences: [{audience, labelUa, customerCount, sharePercentOfAnalyzed,
  averageLtv}]` (4: RealGrowth/PriceGrowth/Declining/**Stable**), `filtersHash`, `calculatedAt`.
- `GET .../audiences/{audience}?sortBy=name|segment|items|check|ltv&sortDescending&page&pageSize`
  → `PriceAudienceTableDto`: `totalCount`, `withPhoneCount`, `rows: [{customerId, name, phone,
  previousSegment/CurrentSegment (+LabelUa each), itemsPerReceiptPrevious/Current,
  typicalCheckCurrent, ltv}]`, `page/pageSize/totalPages`, `sortBy/sortDescending` (echoes
  normalized value), `recommendation: {triggerUa, actionUa, offerUa, cautionUa}`.
- `POST .../audiences/{audience}/explain` → `{explanationUa, model, tokensUsed}` (503 if no
  Claude key). `POST .../exports/audience` (body: audience/from/to/storeIds/unmaskPii) → xlsx file.

**All-time mode** (no period param at all — a separate mode, design doc §10):
- `GET .../all-time?selectedSegment=Tier1..7` → `PriceSegmentsAllTimeOverviewDto`:
  `customersInBase`, `networkAverageCheck`, `purchasesTotal`, `turnoverTotal`,
  `monthlyTrend: [{year, month, medianCheck, itemsPerReceipt}]`,
  `insights: {yoyPercent, last3MonthsTrendPercent, belowPeakPercent, historicalPeakMedianCheck,
  itemsPerReceiptChangePercent}` (all nullable — not enough history yet), `distribution` (7,
  `{segment, rangeLabelUa, customerCount, averageLtv}`), `selectedSegment`, `recommendation`
  (null until a segment is selected — mirrors the competitor's own "оберіть сегмент" prompt).
- `GET .../all-time/customers?segment=Tier1..7&sortBy=name|segment|items|check|purchases|ltv` →
  `AllTimeCustomerTableDto` (same page/sort shape as above, rows have `purchaseCount` instead of
  before/after segment).
- `POST .../all-time/segments/{segment}/explain`, `POST .../exports/all-time`.

**Frequency mode** (`period=30|60|90`, no "all"):
- `GET .../frequency/overview?declineThresholdPercent=` (omit → tenant's saved default) →
  `FrequencyOverviewDto`: `activeCurrentBuyerCount`, `activeBuyerCountChangePercent`,
  `averageFrequencyCurrent/Previous`, `unionPopulationCount`, `atRiskCount`,
  `atRiskPercentOfUnionPopulation` AND `atRiskPercentOfActiveCurrentBuyers` (both denominators
  exposed explicitly — the competitor only ever shows one, ambiguously), `averageSpendCurrentPeriod`,
  `audiences` (4: Sleeping/Declining/Growing/Other).
- `GET .../frequency/audiences/{audience}?minSpend=&maxSpend=&priceSegment=Tier1..7&declineThresholdPercent=`
  → `FrequencyAudienceTableDto`, rows `{customerId, name, phone, previousFrequency,
  currentFrequency, frequencyDeltaAbsolute, frequencyDeltaPercent (nullable — null when
  previousFrequency=0), typicalCheckCurrent (nullable — **always null for Sleeping**, render
  "—"), spendCurrentPeriod, ltv}`. **For `audience=Sleeping`, minSpend/maxSpend/priceSegment
  filter against the customer's PREVIOUS-period figures, not current** (current is always 0 for
  Sleeping by definition) — the displayed columns themselves don't change meaning, only the filter.
- `POST .../frequency/audiences/{audience}/explain`, `POST .../exports/frequency-audience`.

**Settings**: `GET/PUT api/settings/price-segments` (enterprise_admin) →
`PriceSegmentSettingsDto { defaultFrequencyDeclineThresholdPercent, minReceiptsForBoundaries,
updatedAt }` — GET returns proposed defaults (30%, null) before first save, `updatedAt` is
`null` until an actual save happens (not a fake timestamp).

Enums serialize as strings everywhere (`"Tier3"`, `"RealGrowth"`, `"Sleeping"`, etc.) —
`[JsonConverter(typeof(JsonStringEnumConverter))]` on all 3, matching `RfmSegmentKey`'s own
convention.

## Not in scope (per brief, unchanged)

Domain entities/DbContext/migrations untouched beyond reading (PosTransaction's `StoreId` →
`"LocationId"` column mapping confirmed at `AppDbContext.cs:1065`, matches the design doc).
`Features/Loyalty/`, `Features/ConsumerAuth/`, and Фаза 1 RFM code untouched beyond the
already-scoped `PiiMasking` move. No frontend/mobile changes.

## Git

Not committed — working tree left for review (repo convention: main session/user commits).
