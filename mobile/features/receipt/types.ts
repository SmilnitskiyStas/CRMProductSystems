export type ReceiptStatus = 'draft' | 'receiving' | 'completed' | 'cancelled';

export interface ReceiptItem {
  id: string;
  productId: string;
  productName: string;
  barcode: string | null;
  orderedQty: number;
  receivedQty: number;
  unit: string;
  expiryDate: string | null;
  batchNumber: string | null;
  isProcessed: boolean;
}

export interface Receipt {
  id: string;
  number: string;
  supplierId: string;
  supplierName: string;
  storeId: string;
  status: ReceiptStatus;
  items: ReceiptItem[];
  createdAt: string;
  receivedAt: string | null;
}
