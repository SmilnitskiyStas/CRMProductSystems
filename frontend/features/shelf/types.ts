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

export const STATUS_LABEL: Record<BatchStatus, string> = {
  safe: "Норма",
  warning: "Попередження",
  critical: "Критично",
  expired: "Прострочено",
  sold_out: "Продано",
  archived: "Архів",
  needs_verification: "Перевірка",
};

export const STATUS_COLOR: Record<BatchStatus, { bg: string; text: string; dot: string }> = {
  safe: { bg: "#052E16", text: "#4ADE80", dot: "#22C55E" },
  warning: { bg: "#2D2208", text: "#FBBF24", dot: "#F59E0B" },
  critical: { bg: "#2D0F0F", text: "#F87171", dot: "#EF4444" },
  expired: { bg: "#1F0A0A", text: "#DC2626", dot: "#B91C1C" },
  sold_out: { bg: "#111827", text: "#6B7280", dot: "#4B5563" },
  archived: { bg: "#111827", text: "#6B7280", dot: "#4B5563" },
  needs_verification: { bg: "#1E1B2E", text: "#A78BFA", dot: "#7C3AED" },
};
