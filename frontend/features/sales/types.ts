export interface DailySale {
  id: string;
  storeId: string;
  storeName: string;
  productId: string;
  productName: string;
  barcode: string | null;
  date: string; // yyyy-MM-dd
  quantitySold: number;
  quantityEndOfDay: number | null;
  isPromoDay: boolean;
  isAnomaly: boolean;
  source: string; // manual | pos | import
}

export interface UpsertDailySalePayload {
  storeId: string;
  productId: string;
  date: string;
  quantitySold: number;
  quantityEndOfDay: number | null;
  isPromoDay: boolean;
}

export interface SalesFilters {
  storeId?: string;
  productId?: string;
  from?: string;
  to?: string;
}

export interface CsvImportResult {
  created: number;
  updated: number;
  skipped: number;
  errors: string[];
}
