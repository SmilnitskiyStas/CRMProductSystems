// Post-campaign audience analysis — Фаза 4 (TASK-473).
//
// Source of truth for this contract: `.claude/logs/tasks/472_2026-08-05_post-campaign-backend_backend-developer.md`
// plus the actual DTO/controller source, cross-checked directly against
// `backend/ShelfGuard.Application/Features/MarketingAnalytics/PostCampaign/Dtos/PostCampaignDtos.cs`
// and `PostCampaignController.cs`. Wire casing: property NAMES are camelCase (ASP.NET Core
// default), enum VALUES are PascalCase strings (JsonStringEnumConverter) — same convention
// `audience-builder/types.ts` documents. `RfmSegmentKey` itself is NOT redeclared here — it is
// imported directly from `features/marketing-analytics/types.ts` (the Фаза 1 root), per this
// task's brief: reuse SegmentGrid's color language/segment-key type, never fork a second copy of
// the same 11-segment enum.
//
// Self-contained otherwise (does not import price-segments/audience-builder's own filter shapes) —
// same "each feature owns its own types.ts" convention every sibling phase already documents.

import type { RfmSegmentKey, RfmColorTokens } from "../types";
import { RFM_SEGMENT_COLOR_GROUP, RFM_COLOR_PALETTE, RFM_CANNOT_LOSE_THEM_ACCENT, RFM_NO_PURCHASE_COLOR } from "../types";

export type { RfmSegmentKey, RfmColorTokens };
// Re-exported so every component within this feature imports color tokens from its OWN `../types`
// uniformly (this file is the single place that reaches into the Фаза 1 root) — brief's explicit
// instruction to reuse SegmentGrid's color language rather than invent a second copy.
export { RFM_SEGMENT_COLOR_GROUP, RFM_COLOR_PALETTE, RFM_CANNOT_LOSE_THEM_ACCENT, RFM_NO_PURCHASE_COLOR };

// ── RFM segment priority order + labels ─────────────────────────────────────────────────────────
//
// MUST mirror `backend/ShelfGuard.Application/Features/MarketingAnalytics/RfmSegmentCatalog.cs`
// exactly (`AllKeysInPriorityOrder` + `LabelUa`) — this is the fixed row/column axis for the
// migration transition matrix (source doc §27.1: a full grid with a dot for every genuinely empty
// combination, not just the non-zero cells the API actually returns) and the fallback label for any
// axis segment the API response happens not to mention at all (a segment with zero members in
// BOTH before and after never appears in `PostCampaignMigrationDto.beforeDistribution`/
// `afterDistribution`/`matrix`, since those are built server-side from a sparse dictionary — see
// that DTO's DEV comment). "Без покупок" (`null`) always ranks last — worse than every real segment
// (`PostCampaignService.RankOf`), matching how `SegmentGrid.tsx` also always renders it as a
// separate, non-clickable, worst-case card.
export const RFM_PRIORITY_ORDER: RfmSegmentKey[] = [
  "Champions",
  "Loyal",
  "CannotLoseThem",
  "AtRisk",
  "New",
  "PotentialLoyalist",
  "Promising",
  "AboutToSleep",
  "Hibernating",
  "Lost",
  "NeedsAttention",
];

/** Full fixed axis for the migration matrix / distribution donuts: 11 real segments (priority
 * order) + the "Без покупок" null bucket last. */
export const RFM_AXIS: (RfmSegmentKey | null)[] = [...RFM_PRIORITY_ORDER, null];

/** Verbatim copy of `RfmSegmentCatalog.LabelUa` — see this file's header comment for why a static
 * fallback copy is needed (not obtainable from every API response). */
export const RFM_LABELS_UA: Record<RfmSegmentKey, string> = {
  Champions: "Чемпіони",
  Loyal: "Лояльні",
  CannotLoseThem: "Не можна втратити",
  AtRisk: "Під ризиком",
  New: "Нові",
  PotentialLoyalist: "Потенційно лояльні",
  Promising: "Перспективні",
  AboutToSleep: "Засинають",
  Hibernating: "Сплячі",
  Lost: "Втрачені",
  NeedsAttention: "Потребують уваги",
};

export const NO_PURCHASE_LABEL_UA = "Без покупок";

export function rfmLabelOf(key: RfmSegmentKey | null): string {
  return key === null ? NO_PURCHASE_LABEL_UA : RFM_LABELS_UA[key];
}

export function rfmRankOf(key: RfmSegmentKey | null): number {
  return key === null ? RFM_PRIORITY_ORDER.length : RFM_PRIORITY_ORDER.indexOf(key);
}

// ── GET segments (list) ───────────────────────────────────────────────────────────────────────

export interface PostCampaignSegmentListItemDto {
  id: string;
  name: string | null;
  createdAt: string;
  uploadedCount: number;
  matchedCount: number;
  isAnalyzed: boolean;
  afterStart: string | null; // yyyy-MM-dd
  afterEnd: string | null;
  analyzedAt: string | null;
}

// ── POST segments/import ──────────────────────────────────────────────────────────────────────

export type PostCampaignImportMode = "list" | "file";

export interface PostCampaignImportColumnPreviewDto {
  headers: string[];
  detectedColumnIndex: number;
  detectedColumnHeader: string | null;
  sampleRows: (string | null)[][];
}

export interface PostCampaignImportResultDto {
  segmentId: string;
  name: string | null;
  uploadedCount: number;
  matchedCount: number;
  duplicateCount: number;
  unknownCount: number;
  invalidCount: number;
  unknownTokensSample: string[];
  invalidTokensSample: string[];
  columnPreview: PostCampaignImportColumnPreviewDto | null;
  createdAt: string;
}

/** Application-layer request shape — the actual wire call is multipart/form-data (see
 * `api/postCampaign.ts`'s `importSegment`), this just documents the fields it builds a FormData
 * from. Exactly one of `rawText`/`file` must be set. */
export interface PostCampaignImportParams {
  rawText?: string;
  file?: File;
  name?: string;
  columnIndex?: number;
}

// ── POST segments/{id}/analyze ────────────────────────────────────────────────────────────────

export interface PostCampaignAnalyzeRequest {
  afterStart: string; // yyyy-MM-dd
  afterEnd: string;
}

export interface PostCampaignAnalyzeResultDto {
  segmentId: string;
  afterStart: string;
  afterEnd: string;
  beforeStart: string;
  beforeEnd: string;
  segmentHash: string;
  analyzedAt: string;
}

// ── Recommendations (embedded in Summary/Migration responses) ────────────────────────────────

export interface PostCampaignRecommendationDto {
  triggerUa: string;
  actionUa: string;
  offerUa: string;
  cautionUa: string;
}

// ── GET segments/{id}/summary ─────────────────────────────────────────────────────────────────

export interface PostCampaignSummaryDto {
  matchedCount: number;
  moneyBefore: number;
  moneyAfter: number;
  moneyDeltaPercent: number | null;
  buyersAfter: number;
  reactivated: number;
  reactivationRatePercent: number | null;
  inactiveBefore: number;
  retained: number;
  retentionRatePercent: number | null;
  activeBefore: number;
  dropped: number;
  churnRatePercent: number | null;
  notReturned: number;
  recommendation: PostCampaignRecommendationDto;
  segmentHash: string;
  afterStart: string;
  afterEnd: string;
  beforeStart: string;
  beforeEnd: string;
  calculatedAt: string;
}

// ── GET segments/{id}/daily-turnover ──────────────────────────────────────────────────────────

export interface PostCampaignDailyTurnoverPointDto {
  dayIndex: number;
  afterCalendarDate: string;
  beforeAmount: number;
  afterAmount: number;
}

export interface PostCampaignDailyTurnoverDto {
  points: PostCampaignDailyTurnoverPointDto[];
  totalBefore: number;
  totalAfter: number;
  segmentHash: string;
  afterStart: string;
  afterEnd: string;
  beforeStart: string;
  beforeEnd: string;
  calculatedAt: string;
}

// ── GET segments/{id}/rfm-activity ────────────────────────────────────────────────────────────

export interface PostCampaignRfmActivityDto {
  checksBefore: number;
  checksAfter: number;
  checksDeltaPercent: number | null;
  turnoverBefore: number;
  turnoverAfter: number;
  turnoverDeltaPercent: number | null;
  averageCheckBefore: number | null;
  averageCheckAfter: number | null;
  averageCheckDeltaPercent: number | null;
  recencyBeforeDays: number | null;
  recencyAfterDays: number | null;
  recencyDeltaPercent: number | null;
  recencyDenominatorBefore: number;
  recencyDenominatorAfter: number;
  segmentHash: string;
  afterStart: string;
  afterEnd: string;
  beforeStart: string;
  beforeEnd: string;
  calculatedAt: string;
}

// ── GET segments/{id}/customers ───────────────────────────────────────────────────────────────

export interface PostCampaignCustomerRowDto {
  customerId: string;
  name: string;
  phone: string | null;
  checksBefore: number;
  checksAfter: number;
  turnoverBefore: number;
  turnoverAfter: number;
  segmentBefore: RfmSegmentKey | null;
  segmentBeforeLabelUa: string;
  segmentAfter: RfmSegmentKey | null;
  segmentAfterLabelUa: string;
}

export type PostCampaignCustomerSortBy = "checksbefore" | "checksafter" | "turnoverbefore" | "turnoverafter" | "transition";

export interface PostCampaignCustomerTableDto {
  totalCount: number;
  rows: PostCampaignCustomerRowDto[];
  page: number;
  pageSize: number;
  totalPages: number;
  sortBy: string;
  sortDescending: boolean;
  segmentHash: string;
  calculatedAt: string;
}

export interface PostCampaignCustomerTableParams {
  page: number;
  pageSize: number;
  sortBy: PostCampaignCustomerSortBy;
  sortDescending: boolean;
}

// ── GET segments/{id}/migration ───────────────────────────────────────────────────────────────

export interface PostCampaignSegmentDistributionItemDto {
  segment: RfmSegmentKey | null;
  labelUa: string;
  count: number;
  sharePercent: number;
}

export interface PostCampaignMigrationCellDto {
  before: RfmSegmentKey | null;
  beforeLabelUa: string;
  after: RfmSegmentKey | null;
  afterLabelUa: string;
  count: number;
}

export interface PostCampaignMigrationDto {
  beforeDistribution: PostCampaignSegmentDistributionItemDto[];
  afterDistribution: PostCampaignSegmentDistributionItemDto[];
  matrix: PostCampaignMigrationCellDto[];
  upCount: number;
  stableCount: number;
  downCount: number;
  recommendation: PostCampaignRecommendationDto;
  segmentHash: string;
  afterStart: string;
  afterEnd: string;
  beforeStart: string;
  beforeEnd: string;
  calculatedAt: string;
}

// ── POST segments/{id}/explain ────────────────────────────────────────────────────────────────

export interface ExplainPostCampaignResultDto {
  explanationUa: string;
  model: string;
  tokensUsed: number;
}

// ── Exports ────────────────────────────────────────────────────────────────────────────────────

export interface ExportPostCampaignCustomersRequest {
  storeIds: string[] | null;
  unmaskPii: boolean;
}

// ── Frontend-local state ──────────────────────────────────────────────────────────────────────

/** Page-level report tab selector (source doc §3/§14/§18/§25 — Огляд / Активність R/F/M /
 * Міграція сегментів). */
export type PostCampaignReportTab = "overview" | "activity" | "migration";
