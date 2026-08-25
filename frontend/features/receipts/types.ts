export type ReceiptStatus = "draft" | "ordered" | "in_transit" | "received" | "cancelled";

// Opaque sort keys passed straight through to GET /api/receipts's `sortBy` query param —
// must match the backend contract's sort-key strings exactly (TASK-628-frontend brief).
export type ReceiptSortBy = "createdat" | "status" | "supplier" | "destination" | "expectedat";

export interface ReceiptItemDto {
  id: string;
  productId: string;
  productName: string;
  productBarcode: string | null;
  quantityOrdered: number;
  quantityReceived: number | null;
  pricePurchase: number | null;
  expiryDate: string | null;
  batchNumber: string | null;
  discrepancyNotes: string | null;
  isProcessed: boolean;
}

export interface ReceiptDto {
  id: string;
  supplierId: string | null;
  supplierName: string | null;
  destinationStoreId: string;
  destinationStoreName: string;
  viaCentralStore: boolean;
  status: ReceiptStatus;
  expectedAt: string | null;
  receivedAt: string | null;
  notes: string | null;
  createdAt: string;
  items: ReceiptItemDto[];
}

export interface CreateReceiptRequest {
  supplierId?: string | null;
  destinationStoreId: string;
  viaCentralStore: boolean;
  expectedAt?: string | null;
  notes?: string | null;
  items: {
    productId: string;
    quantityOrdered: number;
    pricePurchase?: number | null;
    expiryDate?: string | null;
    batchNumber?: string | null;
  }[];
}

export interface UpdateItemPayload {
  itemId: string;
  quantityReceived?: number | null;
  pricePurchase?: number | null;
  expiryDate?: string | null;
  batchNumber?: string | null;
  discrepancyNotes?: string | null;
}

// Display labels moved to i18n messages under `Dashboard.receipts.status` (i18n Block 2b,
// TASK-380) — this Record<ReceiptStatus, string> is intentionally gone. Components render
// the label via `useTranslations("Dashboard.receipts.status")` keyed by the status value
// itself. Colors stay here since they're not language-dependent.

export const RECEIPT_STATUS_COLOR: Record<ReceiptStatus, { bg: string; text: string }> = {
  draft: { bg: "#111827", text: "#6B7280" },
  ordered: { bg: "#1E1B2E", text: "#A78BFA" },
  in_transit: { bg: "#1C2A3A", text: "#60A5FA" },
  received: { bg: "#052E16", text: "#4ADE80" },
  cancelled: { bg: "#1F0A0A", text: "#F87171" },
};
