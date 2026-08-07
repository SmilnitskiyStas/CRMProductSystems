export interface ExpirySummaryStoreDto {
  storeId: string;
  storeName: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
}

export interface ExpirySummaryDto {
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  needsVerification: number;
  total: number;
  stores: ExpirySummaryStoreDto[];
}

export interface WriteOffByReasonDto {
  reason: string;
  count: number;
  totalLoss: number;
}

export interface WriteOffByDateDto {
  date: string;
  count: number;
  totalLoss: number;
}

export interface WriteOffAnalyticsDto {
  totalDocuments: number;
  totalLoss: number;
  byReason: WriteOffByReasonDto[];
  byDate: WriteOffByDateDto[];
}

export interface MovementByTypeDto {
  movementType: string;
  count: number;
  totalQuantity: number;
}

export interface MovementAnalyticsDto {
  totalMovements: number;
  totalQuantity: number;
  byType: MovementByTypeDto[];
}

export interface ZoneAnalyticsDto {
  zoneId: string;
  zoneName: string;
  zoneType: string;
  storeId: string;
  storeName: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  totalBatches: number;
}

export interface CategoryAnalyticsDto {
  categoryId: string | null;
  categoryName: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  totalBatches: number;
  totalQuantity: number;
}

export interface LossByStoreDto {
  storeId: string;
  storeName: string;
  totalLoss: number;
  writeOffCount: number;
}

export interface LossesDto {
  totalLoss: number;
  totalWriteOffs: number;
  averageLossPerWriteOff: number;
  byStore: LossByStoreDto[];
}

// ── Period comparison (ADR-016) ─────────────────────────────────────────────
// Opt-in via `?compare=true` on the existing endpoints below. When omitted
// (or false) the response shape is unchanged — see the flat DTOs above/below.

export interface WriteOffAnalyticsCompareDto {
  current: WriteOffAnalyticsDto;
  comparison: WriteOffAnalyticsDto;
  totalLossPercentChange: number | null;
}

export interface LossesCompareDto {
  current: LossesDto;
  comparison: LossesDto;
  totalLossPercentChange: number | null;
}

// ── POS Analytics ──────────────────────────────────────────────────────────────

export interface PosAnalyticsSummaryDto {
  totalRevenue: number;
  transactionCount: number;
  averageTicket: number;
  cashRevenue: number;
  cardRevenue: number;
  shiftCount: number;
  from: string;
  to: string;
}

export interface PosRevenueTrendPoint {
  date: string;
  revenue: number;
  transactions: number;
}

export interface PosRevenueTrendDto {
  points: PosRevenueTrendPoint[];
  groupBy: "day" | "week";
}

export interface PosTopProductItem {
  productId: string;
  productName: string;
  barcode: string;
  totalRevenue: number;
  totalQuantity: number;
  transactionCount: number;
}

export interface PosTopProductsDto {
  items: PosTopProductItem[];
}

export interface PosCashierStat {
  cashierId: string;
  cashierName: string;
  totalRevenue: number;
  transactionCount: number;
  averageTicket: number;
  shiftCount: number;
}

export interface PosCashierStatsDto {
  cashiers: PosCashierStat[];
}

// ── Category × product breakdown (interactive analytics + margin plan, TASK-483) ───────────
// MarginAmount/MarginPercent are null both when the caller can't see margin (ADR-027 —
// AnalyticsAuthorization.CanViewMargin resolved false server-side) and when the product itself
// has no Item.PricePurchase on file — the UI never needs to tell these two cases apart (both
// render as an absent/em-dash figure), it only needs to gate rendering of the columns entirely
// on canViewAnalyticsMargin (frontend/lib/roles.ts), never on whether these fields happen to be
// null for a given row.

export interface CategoryProductRowDto {
  productId: string;
  productName: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  totalQuantity: number;
  salesRevenue: number;
  unitsSold: number;
  marginAmount: number | null;
  marginPercent: number | null;
}

export interface CategoryProductBreakdownDto {
  categoryId: string | null;
  categoryName: string;
  products: CategoryProductRowDto[];
}

// ── Losses × product breakdown (interactive analytics + margin plan, TASK-483) ─────────────
// Serves BOTH the losses-by-store and losses-by-reason drill-downs (independent optional
// AND-filters) — no margin fields at all on this DTO (ADR-027 §1: losses aren't margin-gated).

export interface LossByProductRowDto {
  productId: string;
  productName: string;
  quantity: number;
  lossAmount: number;
  sharePercent: number;
}

export interface LossesByProductDto {
  totalLoss: number;
  products: LossByProductRowDto[];
}

// ── POS Analytics — period comparison (ADR-016) ─────────────────────────────

export interface PosAnalyticsSummaryCompareDto {
  current: PosAnalyticsSummaryDto;
  comparison: PosAnalyticsSummaryDto;
  revenuePercentChange: number | null;
  transactionCountPercentChange: number | null;
}

/** Unlike PosRevenueTrendDto, compare mode returns two flat (sparse) point arrays, not a `points` wrapper. */
export interface PosRevenueTrendCompareDto {
  current: PosRevenueTrendPoint[];
  comparison: PosRevenueTrendPoint[];
  groupBy: "day" | "week";
  from: string;
  to: string;
  compareFrom: string;
  compareTo: string;
}

// ── Single-product sales trend (interactive analytics + margin plan, TASK-484) ─────────────
// Row-click drill-down from PosTopProductsTable, rendered inline via the extended
// ProductAnalyticsTab (frontend/features/inventory/components/ProductAnalyticsTab.tsx). No
// compare-mode variant — this is a snapshot trend off a table row, not a page-level KPI trend.
// marginAmount is null both when the caller can't see margin (ADR-027 — server-side
// AnalyticsAuthorization.CanViewMargin resolved false) and when the product itself has no
// Item.PricePurchase on file — same "don't distinguish, just gate on canViewAnalyticsMargin"
// rule as CategoryProductRowDto above.

export interface ProductSalesTrendPointDto {
  date: string;
  revenue: number;
  quantity: number;
  transactionCount: number;
  marginAmount: number | null;
}

export interface ProductSalesTrendDto {
  productId: string;
  productName: string;
  points: ProductSalesTrendPointDto[];
  groupBy: "day" | "week";
}
