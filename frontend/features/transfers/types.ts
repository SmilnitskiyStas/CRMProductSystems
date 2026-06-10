export type TransferStatus = "draft" | "in_transit" | "received" | "cancelled";

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

export const TRANSFER_STATUS_LABEL: Record<TransferStatus, string> = {
  draft: "Чернетка",
  in_transit: "В дорозі",
  received: "Отримано",
  cancelled: "Скасовано",
};

export const TRANSFER_STATUS_COLOR: Record<TransferStatus, { bg: string; text: string }> = {
  draft: { bg: "#111827", text: "#6B7280" },
  in_transit: { bg: "#1C2A3A", text: "#60A5FA" },
  received: { bg: "#052E16", text: "#4ADE80" },
  cancelled: { bg: "#1F0A0A", text: "#F87171" },
};
