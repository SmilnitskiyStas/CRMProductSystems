export type StockStatus = 'safe' | 'warning' | 'critical' | 'expired' | 'sold_out' | 'needs_verification';

export interface StockBatch {
  id: string;
  productId: string;
  productName: string;
  barcode: string | null;
  batchNumber: string | null;
  quantity: number;
  expiryDate: string;
  daysLeft: number;
  status: StockStatus;
  locationId: string;
  zoneName: string | null;
  shelfNumber: number | null;
  lastCheckedAt: string;
  pricePurchase: number | null;
  priceRetail: number | null;
  defaultReimbursementType: 'fixed' | 'percent' | null;
  defaultReimbursementValue: number | null;
}

export interface CatalogProductLookup {
  id: string;
  name: string;
  barcodes: string[];
  /** Compatibility aliases used by the existing POS scanner. */
  barcode: string;
  price?: number;
  status?: string;
  itemType?: string;
  pricePurchase: number | null;
  priceRetail: number | null;
  defaultReimbursementType: 'fixed' | 'percent' | null;
  defaultReimbursementValue: number | null;
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
