export interface AiOrderListItem {
  id: string;
  storeId: string;
  storeName: string;
  generatedAt: string;
  orderDate: string;
  status: "pending" | "partially_accepted" | "accepted" | "rejected";
  itemsCount: number;
  aiModel: string;
  tokensUsed: number | null;
}

export interface AiOrderItem {
  id: string;
  productId: string;
  productName: string;
  barcode: string | null;
  quantityBase: number;
  quantitySuggested: number;
  quantityFinal: number;
  reasoning: string | null;
  confidence: "high" | "medium" | "low" | null;
  factors: string | null;
  wasEdited: boolean;
  editReason: string | null;
}

export interface AiOrder {
  id: string;
  storeId: string;
  storeName: string;
  generatedAt: string;
  orderDate: string;
  status: AiOrderListItem["status"];
  aiModel: string;
  tokensUsed: number | null;
  items: AiOrderItem[];
}

// Labels resolved via `useTranslations("Dashboard.aiOrders.status")` (t.has(status) ? t(status) : status)
// at call sites — this map stays presentation-only (color/bg), same split as
// RECEIPT_STATUS_COLOR/RECEIPT_STATUS_LABEL in features/receipts/types.ts.
export const STATUS_META: Record<AiOrderListItem["status"], { color: string; bg: string }> = {
  pending: { color: "#FBBF24", bg: "#78350F" },
  partially_accepted: { color: "#93C5FD", bg: "#1E3A8A" },
  accepted: { color: "#4ADE80", bg: "#14532D" },
  rejected: { color: "#F87171", bg: "#7F1D1D" },
};
