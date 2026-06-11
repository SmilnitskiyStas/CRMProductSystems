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

export const STATUS_META: Record<AiOrderListItem["status"], { label: string; color: string; bg: string }> = {
  pending: { label: "Очікує", color: "#FBBF24", bg: "#78350F" },
  partially_accepted: { label: "Прийнято зі змінами", color: "#93C5FD", bg: "#1E3A8A" },
  accepted: { label: "Прийнято", color: "#4ADE80", bg: "#14532D" },
  rejected: { label: "Відхилено", color: "#F87171", bg: "#7F1D1D" },
};
