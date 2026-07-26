// Marketing analytics (RFM) — Фаза 1 (TASK-409).
//
// Source of truth for this contract: `.claude/logs/tasks/406_2026-07-26_marketing-analytics-backend_backend-developer.md`
// ("Frontend API-контракт" section) — written by the backend agent specifically so the
// frontend never has to read the C#. All shapes below are camelCase (ASP.NET Core default
// System.Text.Json web policy). Do NOT duplicate these shapes anywhere else — this file is
// the one place the frontend defines the RFM contract (per CLAUDE.md's "never duplicate
// types between frontend and api-contracts").

export type RfmSegmentKey =
  | "Champions"
  | "Loyal"
  | "CannotLoseThem"
  | "AtRisk"
  | "New"
  | "PotentialLoyalist"
  | "Promising"
  | "AboutToSleep"
  | "Hibernating"
  | "Lost"
  | "NeedsAttention";

/** Frontend-only preset. "custom" means "send from/to instead of period" — never sent to the API as a literal value. */
export type MarketingAnalyticsPeriodPreset = "3m" | "6m" | "12m" | "all" | "custom";

/**
 * Shared filter shape — used as-is as the React Query key for every query below (per the
 * brief: "query key = ПОВНИЙ об'єкт фільтра"), so any change here (period, custom dates, or
 * store selection) naturally invalidates every dependent query at once. `storeIds` should
 * always be passed pre-sorted by the caller so equivalent selections hash identically.
 */
export interface MarketingAnalyticsFilters {
  period: MarketingAnalyticsPeriodPreset;
  /** Only meaningful when period === "custom". */
  from?: string;
  /** Only meaningful when period === "custom". */
  to?: string;
  /** Empty = all stores. */
  storeIds: string[];
}

// ── Color language (RFM_ANALYSIS.md §16.2) ──────────────────────────────────────────────
// The competitor's doc defines 6 color *meanings*, not a segment→color table (that mapping
// isn't shown on the page). This is our own, documented mapping of the fixed 11 segment keys
// (+ "no purchase") onto those 6 meanings — see task log 409 for the full rationale. Hex
// values reuse this app's EXISTING semantic colors where the meaning lines up (e.g. #F87171/
// #DC2626 already mean "critical"/"expired" in features/analytics), so the new page reads as
// consistent with the rest of the dashboard rather than inventing a parallel palette.
export type RfmColorGroup = "positive" | "stable" | "potential" | "attention" | "risk" | "dormant";

export const RFM_SEGMENT_COLOR_GROUP: Record<RfmSegmentKey, RfmColorGroup> = {
  Champions: "positive",
  Loyal: "stable",
  PotentialLoyalist: "potential",
  New: "potential",
  Promising: "potential",
  NeedsAttention: "attention",
  AboutToSleep: "attention",
  AtRisk: "risk",
  CannotLoseThem: "risk",
  Hibernating: "dormant",
  Lost: "dormant",
};

export interface RfmColorTokens {
  accent: string;
  bg: string;
  border: string;
}

/** Бірюзовий / Зелений / Синій / Помаранчевий / Червоний(×2 intensities) / Сірий. */
export const RFM_COLOR_PALETTE: Record<RfmColorGroup, RfmColorTokens> = {
  positive: { accent: "#2DD4BF", bg: "#0F2C29", border: "#155E56" },
  stable: { accent: "#4ADE80", bg: "#0F2A1C", border: "#166534" },
  potential: { accent: "#60A5FA", bg: "#0F1F3D", border: "#1D4ED8" },
  attention: { accent: "#FBBF24", bg: "#2A2109", border: "#92400E" },
  risk: { accent: "#F87171", bg: "#2D0A0A", border: "#7F1D1D" },
  dormant: { accent: "#6B7280", bg: "#161B26", border: "#374151" },
};

/** CannotLoseThem is the most urgent "risk" segment (former Champions) — one shade darker
 * than AtRisk, mirroring how features/analytics already uses #F87171 (critical) vs #DC2626
 * (expired) as two intensities of the same red. */
export const RFM_CANNOT_LOSE_THEM_ACCENT = "#DC2626";

/** Separate "no purchase" card (RFM_ANALYSIS.md §5/§7) — not one of the 11 transactional
 * segments, always rendered as its own, non-clickable card (no segment-detail endpoint exists
 * for it). Uses the same "dormant" gray as Hibernating/Lost. */
export const RFM_NO_PURCHASE_COLOR = RFM_COLOR_PALETTE.dormant;

// ── GET /api/marketing-analytics/overview ───────────────────────────────────────────────

export interface RfmOverviewSegmentDto {
  key: RfmSegmentKey;
  labelUa: string;
  shortDescriptionUa: string;
  customerCount: number;
  sharePercentOfPeriodCustomers: number;
  revenue: number;
  sharePercentOfPeriodRevenue: number;
}

export interface RfmNoPurchaseDto {
  customerCount: number;
  sharePercentOfRegisteredBase: number;
}

export interface RfmOverviewDto {
  periodFrom: string;
  periodTo: string;
  periodCustomerCount: number;
  periodRevenue: number;
  registeredCustomerCount: number;
  everPurchasedCustomerCount: number;
  everPurchasedSharePercent: number;
  /** Always exactly 11 entries, in priority order, zero-count segments included. */
  segments: RfmOverviewSegmentDto[];
  noPurchase: RfmNoPurchaseDto;
  filtersHash: string;
  calculatedAt: string;
}

// ── GET /api/marketing-analytics/segments/{key} ─────────────────────────────────────────

export interface RfmTopProductDto {
  rank: number;
  productName: string;
  coveragePercent: number;
  uniqueCustomerCount: number;
  receiptCount: number;
  barcode: string | null;
}

export interface RfmBehaviorDayDto {
  /** 1=Mon..7=Sun (ISO) — frontend owns the label mapping. */
  dayOfWeekIso: number;
  sharePercent: number;
}

export interface RfmBehaviorHourDto {
  /** 0-23, Europe/Kyiv. */
  hour: number;
  sharePercent: number;
}

export interface RfmBehaviorDto {
  peakDayOfWeekIso: number | null;
  peakHour: number | null;
  averageTicket: number;
  receiptCount: number;
  receiptsPerCustomer: number;
  lastVisit: string | null;
  averageRecencyDays: number;
  /** LTV is ALWAYS all-time, independent of the active RFM window — never conflate with the windowed metrics above. */
  averageLtv: number;
  totalLtv: number;
  byDayOfWeek: RfmBehaviorDayDto[];
  byHour: RfmBehaviorHourDto[];
  topPeakHours: RfmBehaviorHourDto[];
}

export interface RfmRecommendationDto {
  triggerUa: string;
  actionUa: string;
  offerUa: string;
  cautionUa: string;
  productsForPromo: string[];
}

export interface RfmSegmentDetailDto {
  key: RfmSegmentKey;
  labelUa: string;
  shortDescriptionUa: string;
  customerCount: number;
  topProducts: RfmTopProductDto[];
  behavior: RfmBehaviorDto;
  recommendation: RfmRecommendationDto;
  filtersHash: string;
  calculatedAt: string;
}

// ── GET .../products/{productName}/affinity ─────────────────────────────────────────────

export interface RfmAffinityItemDto {
  productName: string;
  lift: number;
  bothBuyersCount: number;
  shareAmongAnchorBuyersPercent: number;
  shareAmongSegmentPercent: number;
  barcode: string | null;
}

export interface RfmAffinityResultDto {
  segmentKey: RfmSegmentKey;
  anchorProductName: string;
  items: RfmAffinityItemDto[];
  filtersHash: string;
  calculatedAt: string;
}

// ── GET .../products/{productName}/basket ───────────────────────────────────────────────

export interface RfmBasketItemDto {
  productName: string;
  togetherSharePercent: number;
  bothReceiptsCount: number;
  barcode: string | null;
}

export interface RfmBasketResultDto {
  segmentKey: RfmSegmentKey;
  anchorProductName: string;
  items: RfmBasketItemDto[];
  filtersHash: string;
  calculatedAt: string;
}

// ── POST .../segments/{key}/explain ─────────────────────────────────────────────────────

export interface ExplainRfmSegmentResultDto {
  explanationUa: string;
  model: string;
  tokensUsed: number;
}

// ── Exports (POST, JSON body, response is the raw .xlsx file) ──────────────────────────

interface MarketingAnalyticsExportBaseRequest {
  key: RfmSegmentKey;
  from: string;
  to: string;
  storeIds: string[] | null;
  unmaskPii: boolean;
}

export type SegmentExportRequest = MarketingAnalyticsExportBaseRequest;

export interface ProductBuyersExportRequest extends MarketingAnalyticsExportBaseRequest {
  productName: string;
}

export interface ProductPairBuyersExportRequest extends MarketingAnalyticsExportBaseRequest {
  productName: string;
  pairedProductName: string;
}
