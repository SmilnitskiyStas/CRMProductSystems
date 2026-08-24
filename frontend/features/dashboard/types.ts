export type ItemStatus =
  | "safe"
  | "warning"
  | "critical"
  | "expired"
  | "sold_out"
  | "needs_verification";

export interface DashboardStats {
  safe: number;
  warning: number;
  critical: number;
  expired: number;
}

export interface AttentionItem {
  id: string;
  productId: string;
  name: string;
  sku: string;
  category: string;
  zone: string;
  quantity: number;
  reorderLevel: number;
  status: ItemStatus;
}

export interface StoreZone {
  id: string;
  name: string;
  type: string;
  status: ItemStatus;
  safe: number;
  warning: number;
  critical: number;
}

// ── Period comparison (ADR-016) ─────────────────────────────────────────────

export interface ExpirySummaryCompareStoreDto {
  storeId: string;
  storeName: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
}

export interface ExpirySummaryCompareStat {
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  needsVerification: number;
  total: number;
  stores: ExpirySummaryCompareStoreDto[];
}

export interface ExpirySummaryCompareDto {
  current: ExpirySummaryCompareStat;
  /** null when no snapshot exists yet for that date (first week after deploy, cross-tenant view, etc). */
  previous: ExpirySummaryCompareStat | null;
}

export interface PeriodMetric {
  current: number;
  previous: number;
  percentChange: number | null;
}

export interface WeeklyKpiDto {
  sales: PeriodMetric;
  revenue: PeriodMetric;
  writeOffLoss: PeriodMetric;
}
