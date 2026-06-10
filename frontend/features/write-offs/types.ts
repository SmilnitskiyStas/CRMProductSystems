export type WriteOffStatus = "draft" | "pending_approval" | "approved" | "rejected";
export type WriteOffReason = "expired" | "damaged" | "theft" | "production_loss" | "other";

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
}

export interface WriteOffDto {
  id: string;
  storeId: string;
  storeName: string;
  status: WriteOffStatus;
  reason: WriteOffReason | null;
  totalLossAmount: number | null;
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
  }[];
}

export const WRITE_OFF_STATUS_LABEL: Record<WriteOffStatus, string> = {
  draft: "Чернетка",
  pending_approval: "На затвердженні",
  approved: "Затверджено",
  rejected: "Відхилено",
};

export const WRITE_OFF_STATUS_COLOR: Record<WriteOffStatus, { bg: string; text: string }> = {
  draft: { bg: "#111827", text: "#6B7280" },
  pending_approval: { bg: "#2D2208", text: "#FBBF24" },
  approved: { bg: "#052E16", text: "#4ADE80" },
  rejected: { bg: "#1F0A0A", text: "#F87171" },
};

export const WRITE_OFF_REASON_LABEL: Record<string, string> = {
  expired: "Прострочено",
  damaged: "Пошкоджено",
  theft: "Крадіжка",
  production_loss: "Виробничі втрати",
  other: "Інше",
};
