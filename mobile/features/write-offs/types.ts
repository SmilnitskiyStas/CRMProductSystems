export type WriteOffStatus = 'pending_approval' | 'approved' | 'rejected';
export type WriteOffReason = 'expired' | 'damaged' | 'theft' | 'production_loss' | 'other';

export interface WriteOffItem {
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

export interface WriteOff {
  id: string;
  locationId: string;
  locationName: string;
  status: WriteOffStatus;
  reason: WriteOffReason | null;
  totalLossAmount: number | null;
  pdfUrl: string | null;
  createdAt: string;
  approvedAt: string | null;
  items: WriteOffItem[];
}

export interface CreateWriteOffItemPayload {
  productStockId?: string;
  productId: string;
  quantity: number;
  unitPrice?: number;
}

export interface CreateWriteOffPayload {
  locationId: string;
  reason: WriteOffReason;
  notes?: string;
  items: CreateWriteOffItemPayload[];
}

export const WRITE_OFF_REASON_LABELS: Record<WriteOffReason, string> = {
  expired: 'Прострочено',
  damaged: 'Пошкоджено',
  theft: 'Крадіжка',
  production_loss: 'Виробничі втрати',
  other: 'Інше',
};

export const STATUS_LABELS: Record<WriteOffStatus, string> = {
  pending_approval: 'На затвердженні',
  approved: 'Затверджено',
  rejected: 'Відхилено',
};

export const STATUS_COLORS: Record<WriteOffStatus, string> = {
  pending_approval: 'text-amber-700 bg-amber-100',
  approved: 'text-green-700 bg-green-100',
  rejected: 'text-red-700 bg-red-100',
};
