export type TransferStatus = 'in_transit' | 'received' | 'cancelled';
export type TransferType = 'store_to_store' | 'cs_to_store' | 'store_to_production';

export interface TransferItem {
  id: string;
  productStockId: string;
  productId: string;
  productName: string;
  quantity: number;
  expiryDate: string;
  batchNumber: string | null;
}

export interface Transfer {
  id: string;
  fromLocationId: string;
  fromLocationName: string;
  toLocationId: string;
  toLocationName: string;
  transferType: TransferType | null;
  status: TransferStatus;
  notes: string | null;
  createdAt: string;
  items: TransferItem[];
}

export interface CreateTransferItemPayload {
  productStockId: string;
  quantity: number;
}

export interface CreateTransferPayload {
  fromLocationId: string;
  toLocationId: string;
  transferType?: TransferType;
  notes?: string;
  items: CreateTransferItemPayload[];
}

export interface LocationOption {
  id: string;
  name: string;
  locationType?: string;
}

export const STATUS_LABELS: Record<TransferStatus, string> = {
  in_transit: 'В дорозі',
  received: 'Отримано',
  cancelled: 'Скасовано',
};

export const STATUS_COLORS: Record<TransferStatus, string> = {
  in_transit: 'text-blue-700 bg-blue-100',
  received: 'text-green-700 bg-green-100',
  cancelled: 'text-gray-600 bg-gray-100',
};

export interface DraftTransferItem {
  productStockId: string;
  productId: string;
  productName: string;
  batchNumber: string | null;
  expiryDate: string;
  quantity: number;
  availableQty: number;
}
