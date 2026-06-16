export type StockStatus = 'safe' | 'warning' | 'critical' | 'expired' | 'sold_out' | 'needs_verification';

export interface StockBatch {
  id: string;
  productId: string;
  productName: string;
  barcode: string | null;
  batchNumber: string | null;
  quantity: number;
  unit: string;
  expiryDate: string;
  daysLeft: number;
  status: StockStatus;
  locationId: string;
  zoneName: string | null;
  shelfNumber: string | null;
  lastCheckedAt: string;
}

export interface CreateStockBatchRequest {
  productId: string;
  batchNumber?: string;
  quantity: number;
  expiryDate: string;
  locationId: string;
  zoneId?: string;
  shelfNumber?: string;
}
