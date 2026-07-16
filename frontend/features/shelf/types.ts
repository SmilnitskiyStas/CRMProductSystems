export type BatchStatus =
  | "safe"
  | "warning"
  | "critical"
  | "expired"
  | "sold_out"
  | "archived"
  | "needs_verification";

export interface StockAction {
  type: string;
  label: string;
  targetStoreName: string | null;
  targetStoreId: string | null;
}

export interface ProductStockDto {
  id: string;
  productId: string;
  productName: string;
  productBarcode: string | null;
  storeId: string;
  storeName: string;
  zoneId: string | null;
  zoneName: string | null;
  shelfNumber: number | null;
  batchNumber: string | null;
  quantity: number;
  quantityInitial: number;
  expiryDate: string; // "YYYY-MM-DD"
  daysLeft: number;
  status: BatchStatus;
  sourceType: string | null;
  addedAt: string;
  lastCheckedAt: string;
}

export interface SuggestionDto {
  stockId: string;
  productId: string;
  productName: string;
  storeId: string;
  storeName: string;
  batchNumber: string | null;
  expiryDate: string;
  daysLeft: number;
  quantity: number;
  status: BatchStatus;
  actions: StockAction[];
}

export interface CreateStockRequest {
  productId: string;
  storeId: string;
  zoneId?: string | null;
  shelfNumber?: number | null;
  batchNumber?: string | null;
  quantity: number;
  expiryDate: string; // "YYYY-MM-DD"
  sourceType?: string | null;
  sourceId?: string | null;
}

export interface FefoConsumeRequest {
  productId: string;
  storeId: string;
  quantity: number;
  notes?: string | null;
}

export interface FefoConsumeResult {
  success: boolean;
  quantityConsumed: number;
  quantityShortfall: number;
  batchesConsumed: { stockId: string; batchNumber: string | null; expiryDate: string; quantityTaken: number }[];
  error: string | null;
}

// Display labels moved to i18n messages under `Dashboard.shelf.status` (i18n Block 2a,
// TASK-379) — this Record<BatchStatus, string> is intentionally gone. Components render
// the label via `useTranslations("Dashboard.shelf.status")` keyed by the status value
// itself (e.g. `t("safe")`), since the BatchStatus union already matches the message keys
// 1:1. Colors stay here since they're not language-dependent.

export const STATUS_COLOR: Record<BatchStatus, { bg: string; text: string; dot: string }> = {
  safe: { bg: "#052E16", text: "#4ADE80", dot: "#22C55E" },
  warning: { bg: "#2D2208", text: "#FBBF24", dot: "#F59E0B" },
  critical: { bg: "#2D0F0F", text: "#F87171", dot: "#EF4444" },
  expired: { bg: "#1F0A0A", text: "#DC2626", dot: "#B91C1C" },
  sold_out: { bg: "#111827", text: "#6B7280", dot: "#4B5563" },
  archived: { bg: "#111827", text: "#6B7280", dot: "#4B5563" },
  needs_verification: { bg: "#1E1B2E", text: "#A78BFA", dot: "#7C3AED" },
};
