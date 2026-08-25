export type WriteOffStatus = "draft" | "pending_approval" | "approved" | "rejected";
export type WriteOffReason = "expired" | "damaged" | "theft" | "production_loss" | "other";
export type ReimbursementType = "fixed" | "percent";

// Opaque sort keys passed straight through to GET /api/write-offs's `sortBy` query param —
// must match the backend contract's sort-key strings exactly (TASK-628-frontend brief).
export type WriteOffSortBy = "createdat" | "status" | "reason" | "netloss";

export interface WriteOffItemDto {
  id: string;
  productStockId: string | null;
  productId: string;
  productName: string;
  batchNumber: string | null;
  expiryDate: string | null;
  quantity: number;
  unitPrice: number | null;
  lossAmount: number | null;
  unitPricePurchase: number | null;
  lossAmountPurchase: number | null;
  isReturnedToSupplier: boolean;
  reimbursementType: ReimbursementType | null;
  reimbursementValue: number | null;
  reimbursementAmount: number | null;
}

export interface WriteOffDto {
  id: string;
  storeId: string;
  storeName: string;
  status: WriteOffStatus;
  reason: WriteOffReason | null;
  totalLossAmount: number | null;
  totalLossAmountPurchase: number | null;
  totalReimbursementAmount: number | null;
  netLossAmount: number | null;
  pdfUrl: string | null;
  createdAt: string;
  approvedAt: string | null;
  items: WriteOffItemDto[];
}

export interface CreateWriteOffRequest {
  storeId: string;
  reason?: string | null;
  notes?: string | null;
  items: {
    productStockId?: string | null;
    productId: string;
    quantity: number;
    unitPrice?: number | null;
    isReturnedToSupplier?: boolean;
    reimbursementType?: ReimbursementType | null;
    reimbursementValue?: number | null;
  }[];
}

// Display labels moved to i18n messages under `Dashboard.writeOffs.status` /
// `Dashboard.writeOffs.reason` (i18n Block 2b, TASK-380) — these Record<...,string> maps
// are intentionally gone. Components render labels via
// `useTranslations("Dashboard.writeOffs.status")` / `"Dashboard.writeOffs.reason"` keyed by
// the status/reason value itself. Colors stay here since they're not language-dependent.

export const WRITE_OFF_STATUS_COLOR: Record<WriteOffStatus, { bg: string; text: string }> = {
  draft: { bg: "#111827", text: "#6B7280" },
  pending_approval: { bg: "#2D2208", text: "#FBBF24" },
  approved: { bg: "#052E16", text: "#4ADE80" },
  rejected: { bg: "#1F0A0A", text: "#F87171" },
};

export const WRITE_OFF_REASON_VALUES = [
  "expired",
  "damaged",
  "theft",
  "production_loss",
  "other",
] as const;
