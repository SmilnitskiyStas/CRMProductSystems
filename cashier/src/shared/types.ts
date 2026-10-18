// Types shared by the main process, the preload bridge and the renderer.

export type ErrorKind =
  | 'network' // server unreachable / timeout
  | 'unauthorized' // 401: bad device token, bad PIN or expired operator session
  | 'locked' // 423: PIN lockout / expired goods
  | 'conflict' // 409
  | 'validation' // 400/403/404/other 4xx
  | 'server'; // 5xx

export interface ApiError {
  kind: ErrorKind;
  status?: number;
  message: string;
}

export type Result<T> = { ok: true; data: T } | { ok: false; error: ApiError };

export interface RegisterInfo {
  registerId: string;
  number: number;
  name: string;
  type: string;
  storeId: string;
  storeName: string | null;
}

export interface OperatorInfo {
  cashierId: string;
  cashierName: string;
  sessionId: string;
  expiresAt: string;
}

export interface AppState {
  version: string;
  serverUrl: string;
  register: RegisterInfo | null;
  operator: OperatorInfo | null;
  online: boolean;
}

export interface ShiftInfo {
  shiftId: string;
  storeId: string;
  status: string;
  openedAt: string;
  closedAt: string | null;
  totalSales: number;
  shiftNumber: number | null;
  openingCash: number | null;
  closingCash: number | null;
  expectedCashAmount: number | null;
  cashDiscrepancy: number | null;
}

export interface CatalogItem {
  id: string;
  name: string;
  barcodes: string[];
  price: number | null;
  vatRate: number;
  unit: string;
}

export interface CatalogStatus {
  count: number;
  lastSyncAt: string | null;
  syncing: boolean;
  error: string | null;
}

export type PaymentType = 'Cash' | 'Card';

export interface SaleLineRequest {
  barcode: string;
  quantity: number;
}

export interface SaleSubmitRequest {
  clientTransactionId: string;
  shiftId: string;
  items: SaleLineRequest[];
  paymentType: PaymentType;
  paymentAmount: number;
}

export interface SaleReceipt {
  transactionId: string;
  receiptNumber: string;
  subtotal: number;
  paymentType: string;
  paymentAmount: number;
  change: number;
  fiscalStatus: string;
  fiscalNumber: string | null;
  createdAt: string;
  items: { name: string; barcode: string; quantity: number; unitPrice: number; total: number }[];
}

/** Outcome of a sale submit, classified like mobile/features/pos/saleSubmission.ts. */
export type SaleFailureKind = 'conflict' | 'locked' | 'uncertain' | 'failed' | 'unauthorized';
export type SaleGatewayResult =
  | { ok: true; sale: SaleReceipt }
  | { ok: false; kind: SaleFailureKind; message: string };

export interface CashierApi {
  getState(): Promise<AppState>;
  activate(serverUrl: string, activationCode: string): Promise<Result<RegisterInfo>>;
  login(pin: string): Promise<Result<OperatorInfo>>;
  logout(reason: 'logout' | 'idle'): Promise<void>;
  getCurrentShift(): Promise<Result<ShiftInfo | null>>;
  openShift(openingCash: number): Promise<Result<ShiftInfo>>;
  closeShift(actualClosingCash: number): Promise<Result<ShiftInfo>>;
  catalogStatus(): Promise<CatalogStatus>;
  syncCatalog(): Promise<CatalogStatus>;
  lookupBarcode(barcode: string): Promise<CatalogItem | null>;
  searchCatalog(query: string): Promise<CatalogItem[]>;
  submitSale(request: SaleSubmitRequest): Promise<SaleGatewayResult>;
  onOnlineChange(cb: (online: boolean) => void): () => void;
  onSessionExpired(cb: () => void): () => void;
  onCatalogChange(cb: (status: CatalogStatus) => void): () => void;
}

declare global {
  interface Window {
    cashier: CashierApi;
  }
}
