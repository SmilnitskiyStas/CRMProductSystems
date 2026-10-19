import type { PromotionRule } from './promotions/engine';

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

export interface ConnectionState {
  online: boolean;
  checking: boolean;
  /** ISO time of the next automatic probe (drives the countdown); null while a probe runs. */
  nextProbeAt: string | null;
}

export interface AppState {
  version: string;
  serverUrl: string;
  register: RegisterInfo | null;
  operator: OperatorInfo | null;
  online: boolean;
  connection: ConnectionState;
  queue: QueueSummary;
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
  /** Target of category-wide promotion rules (catalog schema v2). */
  categoryId?: string | null;
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
  items: {
    name: string;
    barcode: string;
    quantity: number;
    unitPrice: number;
    total: number;
    discount?: number;
    /** Receipt label of the promotion that priced this (sub-)line. */
    promoLabel?: string | null;
    /** Shelf price per unit before promotions (struck through in the UI). */
    regularUnitPrice?: number;
  }[];
  /** Set when the server re-priced an offline/provisional sale: the total the cashier saw first. */
  localTotal?: number;
  /** True while the receipt number is the provisional local one (L-<register>-<seq>) and not yet confirmed by the server. */
  local?: boolean;
  clientTransactionId?: string;
  localReceiptNo?: string;
  storeName?: string | null;
  registerNumber?: number;
  registerName?: string;
  cashierName?: string;
}

/** Outcome of a sale submit, classified like mobile/features/pos/saleSubmission.ts. */
export type SaleFailureKind = 'conflict' | 'locked' | 'uncertain' | 'failed' | 'unauthorized';
export type SaleGatewayResult =
  | { ok: true; sale: SaleReceipt }
  | { ok: false; kind: SaleFailureKind; message: string };

export type OutboxStatus = 'pending' | 'sending' | 'synced' | 'rejected' | 'dismissed';

export interface QueueEntry {
  clientTransactionId: string;
  status: OutboxStatus;
  attempts: number;
  lastError: string | null;
  createdAt: string;
  nextAttemptAt: string | null;
  localReceiptNo: string;
  serverReceiptNo: string | null;
  total: number;
  paymentType: string;
  itemCount: number;
  cashierName: string | null;
  /** Server total after sync when it differs from what the cashier saw (re-priced by the server). */
  serverTotal: number | null;
  totalDifference: number | null;
}

export interface QueueSummary {
  pending: number; // pending + sending
  rejected: number;
  needsLogin: boolean;
  syncing: boolean;
  lastSyncAt: string | null;
  lastError: string | null;
}

export type PrintTransport = 'windows' | 'tcp' | 'serial' | 'file';
export type PrintCodepage = 'cp866' | 'cp1125' | 'cp1251' | 'utf8';

export interface PrintSettings {
  autoPrint: boolean;
  transport: PrintTransport;
  printerName: string; // windows
  tcpHost: string;
  tcpPort: number;
  serialPort: string; // e.g. COM3
  serialBaud: number;
  filePath: string; // virtual transport: raw bytes appended to this file
  codepage: PrintCodepage;
  /** Printer-specific ESC t n page number for the chosen codepage (Epson: 17=CP866, 46=WPC1251). */
  codepageId: number;
  widthMm: 58 | 80;
  copies: number;
  openDrawerOnCash: boolean;
}

export interface PrintResult {
  clientTransactionId?: string;
  ok: boolean;
  error?: string;
}

export interface PromotionsState {
  rules: PromotionRule[];
  etag: string | null;
  fetchedAt: string | null;
  syncing: boolean;
  error: string | null;
}

export interface CameraPrefs {
  enabled: boolean;
  deviceId: string | null;
}

/** General app preferences (userData/app-prefs.json). */
export interface AppPrefs {
  /** Auto sign-out after this many minutes without input (MIN..MAX). */
  idleLogoutMinutes: number;
}

export const DEFAULT_IDLE_LOGOUT_MINUTES = 15;
export const MIN_IDLE_LOGOUT_MINUTES = 1;
export const MAX_IDLE_LOGOUT_MINUTES = 120;

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
  queueList(): Promise<QueueEntry[]>;
  queueSummary(): Promise<QueueSummary>;
  queueSyncNow(): Promise<QueueSummary>;
  queueRetry(clientTransactionId: string): Promise<QueueSummary>;
  queueDismiss(clientTransactionId: string): Promise<QueueSummary>;
  getPrintSettings(): Promise<PrintSettings>;
  savePrintSettings(settings: PrintSettings): Promise<PrintSettings>;
  listPrinters(): Promise<string[]>;
  testPrint(): Promise<PrintResult>;
  printReceipt(clientTransactionId: string): Promise<PrintResult>;
  openDrawer(): Promise<PrintResult>;
  getPromotions(): Promise<PromotionsState>;
  syncPromotions(): Promise<PromotionsState>;
  onPromotionsChange(cb: (state: PromotionsState) => void): () => void;
  getCameraPrefs(): Promise<CameraPrefs>;
  saveCameraPrefs(prefs: CameraPrefs): Promise<CameraPrefs>;
  getAppPrefs(): Promise<AppPrefs>;
  saveAppPrefs(prefs: AppPrefs): Promise<AppPrefs>;
  onOnlineChange(cb: (state: ConnectionState) => void): () => void;
  retryConnection(): Promise<ConnectionState>;
  checkServer(url: string): Promise<{ reachable: boolean; message: string | null }>;
  onSessionExpired(cb: () => void): () => void;
  onCatalogChange(cb: (status: CatalogStatus) => void): () => void;
  onQueueChange(cb: (summary: QueueSummary) => void): () => void;
  onPrintResult(cb: (result: PrintResult) => void): () => void;
}

declare global {
  interface Window {
    cashier: CashierApi;
  }
}
