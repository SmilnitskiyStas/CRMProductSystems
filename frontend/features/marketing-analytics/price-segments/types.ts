// Price segments + frequency & reactivation — Фаза 2 (TASK-421).
//
// Source of truth for this contract: `.claude/logs/tasks/420_2026-07-27_price-segments-backend_backend-developer.md`
// ("API contract for the next frontend agent" section) — NOT the earlier scratchpad design doc.
// The backend was implemented after that doc and differs in a few small ways (confirmed by
// reading the actual DTOs/controller/enums in `backend/ShelfGuard.Application/Features/
// MarketingAnalytics/PriceSegments/` and `backend/ShelfGuard.Api/Controllers/PriceSegments*.cs`).
// All shapes below are camelCase (ASP.NET Core default web policy, no custom JsonNamingPolicy —
// same convention already used by `features/marketing-analytics/types.ts`). Do NOT duplicate
// these shapes anywhere else (CLAUDE.md: never duplicate types between frontend and
// api-contracts).
//
// This feature deliberately does NOT import from `features/marketing-analytics/types.ts` (the
// RFM feature) even though the two are visually related — each feature owns its own `types.ts`
// per the project convention, so this one stays self-contained/portable.

// ── Enums (serialize as strings — JsonStringEnumConverter on the backend, confirmed in
// PriceSegmentKey.cs / PriceAudienceKey.cs / FrequencyAudienceKey.cs) ───────────────────────────

/** 7 network-wide price tiers, Tier1 = lowest .. Tier7 = open-ended top tier. Ordinal, NOT
 * semantic — the actual ₴ boundaries are per-tenant and live; always use the server-provided
 * `rangeLabelUa`/`segmentLabelUa` for display, never hardcode a currency figure here. */
export type PriceSegmentKey = "Tier1" | "Tier2" | "Tier3" | "Tier4" | "Tier5" | "Tier6" | "Tier7";

export const ALL_PRICE_TIERS: PriceSegmentKey[] = ["Tier1", "Tier2", "Tier3", "Tier4", "Tier5", "Tier6", "Tier7"];

/** Comparison-mode ("Суми покупок") audiences. Stable gets full parity with the other three on
 * purpose (list/sort/paginate/export/recommendation) — the competitor never exposes it as a real
 * audience (analysis doc §7.4/§25.3); this app closes that gap deliberately. */
export type PriceAudienceKey = "RealGrowth" | "PriceGrowth" | "Declining" | "Stable";

/** "Частота та реактивація" audiences. Population = UNION of current+previous buyers (never the
 * intersection the comparison tab's "Проаналізовано" cohort uses) — a Sleeping customer has zero
 * current-period purchases by definition, so an intersection would exclude them entirely. */
export type FrequencyAudienceKey = "Sleeping" | "Declining" | "Growing" | "Other";

// ── Color language — feature-local palette (reuses the same hex values RFM/features/analytics
// already established for each meaning, so the app still reads as one consistent system, without
// this feature importing another feature's types.ts). ──────────────────────────────────────────

export interface PriceColorTokens {
  accent: string;
  bg: string;
  border: string;
}

export const PRICE_AUDIENCE_COLORS: Record<PriceAudienceKey, PriceColorTokens> = {
  RealGrowth: { accent: "#2DD4BF", bg: "#0F2C29", border: "#155E56" }, // positive — confirmed real behavior change
  PriceGrowth: { accent: "#60A5FA", bg: "#0F1F3D", border: "#1D4ED8" }, // potential — growth not yet confirmed real
  Declining: { accent: "#F87171", bg: "#2D0A0A", border: "#7F1D1D" }, // risk
  Stable: { accent: "#4ADE80", bg: "#0F2A1C", border: "#166534" }, // stable
};

/** Sleeping is the MORE urgent of the two "losing" audiences (analysis doc: "терміновий
 * win-back" vs Declining's "м'яке нагадування") — red goes to Sleeping, amber to Declining. */
export const FREQUENCY_AUDIENCE_COLORS: Record<FrequencyAudienceKey, PriceColorTokens> = {
  Growing: { accent: "#2DD4BF", bg: "#0F2C29", border: "#155E56" }, // positive
  Other: { accent: "#4ADE80", bg: "#0F2A1C", border: "#166534" }, // stable
  Declining: { accent: "#FBBF24", bg: "#2A2109", border: "#92400E" }, // attention
  Sleeping: { accent: "#F87171", bg: "#2D0A0A", border: "#7F1D1D" }, // risk — zero current-period purchases
};

// ── Filters ──────────────────────────────────────────────────────────────────────────────────

export type PriceSegmentsPeriodPreset = "30" | "60" | "90" | "custom";

/**
 * Shared by BOTH the "Суми покупок" and "Частота та реактивація" tabs — mirrors the competitor's
 * own single top period+store control governing both of its tabs (analysis doc §4.1). Only
 * "Весь час" is pulled out into its own separate MODE TAB here, never a period preset in this
 * bar (design doc §6 / task log 420: all-time is a genuinely different mode, no period concept
 * at all — a different KPI set, no comparison cohort).
 */
export interface PriceSegmentsPeriodFilters {
  period: PriceSegmentsPeriodPreset;
  /** Only meaningful when period === "custom". */
  from?: string;
  /** Only meaningful when period === "custom". */
  to?: string;
  /** Empty = all stores. */
  storeIds: string[];
}

// ── Sort keys — mirror backend's PriceSegmentSortKeys allowlists exactly ───────────────────────

export type PriceAudienceSortBy = "name" | "segment" | "items" | "check" | "ltv";
export type AllTimeSortBy = "name" | "segment" | "items" | "check" | "purchases" | "ltv";
export type FrequencySortBy = "name" | "previous" | "current" | "delta" | "check" | "spend" | "ltv";

// ── Recommendation — shared shape (task log 420: "same shape as RfmRecommendationDto but no
// ProductsForPromo" — Фаза 2 has no per-audience top-products query to anchor a cross-sell list). ─

export interface PriceSegmentRecommendationDto {
  triggerUa: string;
  actionUa: string;
  offerUa: string;
  cautionUa: string;
}

// ── Comparison mode: GET price-segments/overview ────────────────────────────────────────────

export interface PriceSegmentDistributionPointDto {
  segment: PriceSegmentKey;
  rangeLabelUa: string;
  currentCount: number;
  previousCount: number;
}

export interface PriceAudienceSummaryDto {
  audience: PriceAudienceKey;
  labelUa: string;
  customerCount: number;
  sharePercentOfAnalyzed: number;
  averageLtv: number;
}

/**
 * `analyzedCount` (comparison cohort — bought in BOTH windows), `currentPeriodBuyerCount` and
 * `previousPeriodBuyerCount` (each window's own active-buyer count, i.e. the chart's own two
 * series totals) are THREE different denominators and are NEVER equal — task log 420 / analysis
 * doc §6.2/§25.1 documents the competitor's own UI blurring this distinction. Always label all
 * three separately, never conflate.
 */
export interface PriceSegmentsOverviewDto {
  periodFrom: string;
  periodTo: string;
  previousPeriodFrom: string;
  previousPeriodTo: string;
  analyzedCount: number;
  currentPeriodBuyerCount: number;
  previousPeriodBuyerCount: number;
  raisedCount: number;
  declinedCount: number;
  stableCount: number;
  priceIndexPercent: number;
  /** Always exactly 7 entries, in tier order, zero-count tiers included. */
  distribution: PriceSegmentDistributionPointDto[];
  /** Always exactly 4: RealGrowth/PriceGrowth/Declining/Stable. */
  audiences: PriceAudienceSummaryDto[];
  filtersHash: string;
  calculatedAt: string;
}

// ── Comparison mode: GET price-segments/audiences/{audience} ────────────────────────────────

/** `previousSegmentLabelUa`/`currentSegmentLabelUa` are CURRENCY RANGE labels (e.g. "190–280 ₴"),
 * not audience names — confirmed via `PriceSegmentCatalog.RangeLabelUa` call sites in
 * PriceSegmentsService.cs. */
export interface PriceAudienceRowDto {
  customerId: string;
  name: string;
  phone: string | null;
  previousSegment: PriceSegmentKey;
  previousSegmentLabelUa: string;
  currentSegment: PriceSegmentKey;
  currentSegmentLabelUa: string;
  itemsPerReceiptPrevious: number;
  itemsPerReceiptCurrent: number;
  typicalCheckCurrent: number;
  ltv: number;
}

export interface PriceAudienceTableDto {
  audience: PriceAudienceKey;
  labelUa: string;
  totalCount: number;
  withPhoneCount: number;
  rows: PriceAudienceRowDto[];
  page: number;
  pageSize: number;
  totalPages: number;
  sortBy: string;
  sortDescending: boolean;
  recommendation: PriceSegmentRecommendationDto;
  filtersHash: string;
  calculatedAt: string;
}

// ── Explain — shared result shape across all 3 explain flows ───────────────────────────────

export interface ExplainPriceSegmentResultDto {
  explanationUa: string;
  model: string;
  tokensUsed: number;
}

// ── Exports (POST, JSON body, response is the raw .xlsx file) ──────────────────────────────

export interface ExportPriceAudienceRequest {
  audience: PriceAudienceKey;
  from: string;
  to: string;
  storeIds: string[] | null;
  unmaskPii: boolean;
}

export interface ExportAllTimeRequest {
  segment: PriceSegmentKey | null;
  storeIds: string[] | null;
  unmaskPii: boolean;
}

export interface ExportFrequencyAudienceRequest {
  audience: FrequencyAudienceKey;
  from: string;
  to: string;
  storeIds: string[] | null;
  declineThresholdPercent: number | null;
  minSpend: number | null;
  maxSpend: number | null;
  priceSegmentFilter: PriceSegmentKey | null;
  unmaskPii: boolean;
}

// ── All-time mode: GET price-segments/all-time ──────────────────────────────────────────────

export interface MedianCheckMonthPointDto {
  year: number;
  month: number;
  medianCheck: number;
  itemsPerReceipt: number;
}

export interface AllTimeSegmentDistributionDto {
  segment: PriceSegmentKey;
  rangeLabelUa: string;
  customerCount: number;
  averageLtv: number;
}

/** All fields nullable — each needs 2+ comparable history points a brand-new tenant won't have
 * yet. null means "not enough history yet", never zero. */
export interface AllTimeInsightsDto {
  yoyPercent: number | null;
  last3MonthsTrendPercent: number | null;
  belowPeakPercent: number | null;
  historicalPeakMedianCheck: number | null;
  itemsPerReceiptChangePercent: number | null;
}

/** `recommendation` is null until `selectedSegment` is set — mirrors the competitor's own
 * "Оберіть ціновий сегмент нижче — рекомендація підлаштується під нього" prompt-before-selection
 * state (task log 420 / analysis doc §14), NOT missing data. */
export interface PriceSegmentsAllTimeOverviewDto {
  customersInBase: number;
  networkAverageCheck: number;
  purchasesTotal: number;
  turnoverTotal: number;
  monthlyTrend: MedianCheckMonthPointDto[];
  insights: AllTimeInsightsDto;
  /** Always exactly 7 entries, in tier order — sums to `customersInBase` with no remainder. */
  distribution: AllTimeSegmentDistributionDto[];
  selectedSegment: PriceSegmentKey | null;
  recommendation: PriceSegmentRecommendationDto | null;
  filtersHash: string;
  calculatedAt: string;
}

// ── All-time mode: GET price-segments/all-time/customers ────────────────────────────────────

export interface AllTimeCustomerRowDto {
  customerId: string;
  name: string;
  phone: string | null;
  segment: PriceSegmentKey;
  segmentLabelUa: string;
  itemsPerReceipt: number;
  typicalCheck: number;
  purchaseCount: number;
  ltv: number;
}

export interface AllTimeCustomerTableDto {
  segment: PriceSegmentKey | null;
  totalCount: number;
  rows: AllTimeCustomerRowDto[];
  page: number;
  pageSize: number;
  totalPages: number;
  sortBy: string;
  sortDescending: boolean;
  filtersHash: string;
  calculatedAt: string;
}

// ── Frequency mode: GET price-segments/frequency/overview ──────────────────────────────────

/** `sharePercent` is of the UNION population (current ∪ previous buyers) — frequency's natural
 * denominator, NOT "Проаналізовано" (that's the comparison tab's intersection concept — a
 * deliberately different population). */
export interface FrequencyAudienceSummaryDto {
  audience: FrequencyAudienceKey;
  labelUa: string;
  customerCount: number;
  sharePercent: number;
  averageLtv: number;
}

/**
 * `atRiskPercentOfUnionPopulation` and `atRiskPercentOfActiveCurrentBuyers` are the SAME
 * numerator (Sleeping ∪ Declining) over two DIFFERENT denominators — task log 420 / analysis doc
 * §17.6/§25.2: the competitor only ever shows the arguably-misleading "% of active current
 * buyers" figure (Sleeping customers have zero CURRENT purchases). Always render both, never
 * pick one.
 */
export interface FrequencyOverviewDto {
  periodFrom: string;
  periodTo: string;
  previousPeriodFrom: string;
  previousPeriodTo: string;
  activeCurrentBuyerCount: number;
  activeBuyerCountChangePercent: number;
  averageFrequencyCurrent: number;
  averageFrequencyPrevious: number;
  unionPopulationCount: number;
  atRiskCount: number;
  atRiskPercentOfUnionPopulation: number;
  atRiskPercentOfActiveCurrentBuyers: number;
  averageSpendCurrentPeriod: number;
  /** Always exactly 4: Sleeping/Declining/Growing/Other. */
  audiences: FrequencyAudienceSummaryDto[];
  filtersHash: string;
  calculatedAt: string;
}

// ── Frequency mode: GET price-segments/frequency/audiences/{audience} ──────────────────────

/**
 * `typicalCheckCurrent` is null whenever the customer has zero CURRENT-period receipts (always
 * true for every Sleeping row) — render "—", NEVER "0 ₴". `frequencyDeltaPercent` is null
 * whenever `previousFrequency` is 0 — render "—", NEVER "∞". `spendCurrentPeriod` is ALWAYS the
 * current period's spend regardless of audience (0 for Sleeping, by definition) — only the
 * table's FILTER (minSpend/maxSpend/priceSegmentFilter) re-orients to the previous period for
 * Sleeping, never this displayed column.
 */
export interface FrequencyAudienceRowDto {
  customerId: string;
  name: string;
  phone: string | null;
  previousFrequency: number;
  currentFrequency: number;
  frequencyDeltaAbsolute: number;
  frequencyDeltaPercent: number | null;
  typicalCheckCurrent: number | null;
  spendCurrentPeriod: number;
  ltv: number;
}

export interface FrequencyAudienceTableDto {
  audience: FrequencyAudienceKey;
  labelUa: string;
  totalCount: number;
  withPhoneCount: number;
  rows: FrequencyAudienceRowDto[];
  page: number;
  pageSize: number;
  totalPages: number;
  sortBy: string;
  sortDescending: boolean;
  recommendation: PriceSegmentRecommendationDto;
  filtersHash: string;
  calculatedAt: string;
}

// ── Settings: GET/PUT api/settings/price-segments ───────────────────────────────────────────

export interface PriceSegmentSettingsDto {
  defaultFrequencyDeclineThresholdPercent: number;
  minReceiptsForBoundaries: number | null;
  updatedAt: string | null;
}

export interface UpsertPriceSegmentSettingsRequest {
  defaultFrequencyDeclineThresholdPercent: number;
  minReceiptsForBoundaries: number | null;
}

// ── Request param bags (frontend-local — shape the paginated-table hooks' inputs; never sent
// to the wire as-is, the api/ layer maps these onto the actual query string). ──────────────────

export interface PriceAudienceTableParams {
  audience: PriceAudienceKey;
  page: number;
  pageSize: number;
  sortBy: PriceAudienceSortBy;
  sortDescending: boolean;
}

export interface AllTimeCustomerTableParams {
  segment: PriceSegmentKey | null;
  page: number;
  pageSize: number;
  sortBy: AllTimeSortBy;
  sortDescending: boolean;
}

export interface FrequencyAudienceTableParams {
  audience: FrequencyAudienceKey;
  minSpend?: number;
  maxSpend?: number;
  priceSegmentFilter?: PriceSegmentKey;
  declineThresholdPercent?: number;
  page: number;
  pageSize: number;
  sortBy: FrequencySortBy;
  sortDescending: boolean;
}
