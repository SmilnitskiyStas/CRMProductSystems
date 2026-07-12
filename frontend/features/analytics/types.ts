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
