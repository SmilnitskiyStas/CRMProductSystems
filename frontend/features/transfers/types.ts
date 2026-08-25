export type TransferStatus = "draft" | "in_transit" | "received" | "cancelled";

// Opaque sort keys passed straight through to GET /api/transfers's `sortBy` query param —
// must match the backend contract's sort-key strings exactly (TASK-628-frontend brief).
export type TransferSortBy = "createdat" | "status" | "from" | "to";

export interface TransferItemDto {
  id: string;
  productStockId: string;
  productId: string;
  productName: string;
  quantity: number;
  expiryDate: string;
  batchNumber: string | null;
}

export interface TransferDto {
  id: string;
  fromStoreId: string;
  fromStoreName: string;
  toStoreId: string;
  toStoreName: string;
  transferType: string | null;
  status: TransferStatus;
  notes: string | null;
  createdAt: string;
  items: TransferItemDto[];
}

export interface CreateTransferRequest {
  fromStoreId: string;
  toStoreId: string;
  transferType?: string | null;
  notes?: string | null;
  items: { productStockId: string; quantity: number }[];
}

// Display labels moved to i18n messages under `Dashboard.transfers.status` (i18n Block 2b,
// TASK-380) — this Record<TransferStatus, string> is intentionally gone. Components render
// the label via `useTranslations("Dashboard.transfers.status")` keyed by the status value
// itself. Colors stay here since they're not language-dependent.

export const TRANSFER_STATUS_COLOR: Record<TransferStatus, { bg: string; text: string }> = {
  draft: { bg: "#111827", text: "#6B7280" },
  in_transit: { bg: "#1C2A3A", text: "#60A5FA" },
  received: { bg: "#052E16", text: "#4ADE80" },
  cancelled: { bg: "#1F0A0A", text: "#F87171" },
};
