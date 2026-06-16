// Aligned with backend ReceiptDto / ReceiptItemDto (api-contracts.md).
export type ReceiptStatus = 'draft' | 'in_transit' | 'received' | 'cancelled';

export interface ReceiptItem {
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

export interface Receipt {
  id: string;
  supplierId: string | null;
  supplierName: string | null;
  destinationLocationId: string;
  destinationLocationName: string;
  viaCentralStore: boolean;
  status: ReceiptStatus;
  expectedAt: string | null;
  receivedAt: string | null;
  notes: string | null;
  createdAt: string;
  items: ReceiptItem[];
}

/** Human-readable short number derived from the id (backend has no number field). */
export function receiptNumber(r: Pick<Receipt, 'id'>): string {
  return r.id.slice(0, 8).toUpperCase();
}
