export type ShiftStatus =
  | "Opening"
  | "Open"
  | "OpenFailed"
  | "Closing"
  | "Closed"
  | "CloseFailed";

export type FiscalStatus =
  | "pending_fiscalization"
  | "fiscalized"
  | "fiscalization_failed";

export type PaymentType = "Cash" | "Card";

export interface ShiftDto {
  shiftId: string;
  storeId: string;
  status: ShiftStatus;
  openedAt: string;
  closedAt: string | null;
  providerShiftId: string | null;
  fiscalStatus: string;
  totalSales: number;
  shiftNumber: number | null;
  /** Populated as soon as the shift is opened (mirrors OpenShiftRequest.openingCash). */
  openingCash: number | null;
  /** Actual counted cash entered at close — null unless a reconciled close was requested. */
  closingCash: number | null;
  /** openingCash + this shift's cash-only sales. Server-computed, null until a reconciled close. */
  expectedCashAmount: number | null;
  /** closingCash - expectedCashAmount. Positive = surplus, negative = shortage, 0 = exact match. */
  cashDiscrepancy: number | null;
}

export interface SaleItemDto {
  productId: string;
  productName: string;
  barcode: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  total: number;
}

export interface SaleDto {
  transactionId: string;
  shiftId: string;
  items: SaleItemDto[];
  subtotal: number;
  paymentType: PaymentType;
  paymentAmount: number;
  change: number;
  fiscalStatus: FiscalStatus;
  fiscalNumber: string | null;
  receiptNumber: string;
  createdAt: string;
}

export interface ShiftSalesResponse {
  items: SaleDto[];
  totalAmount: number;
}

export interface OpenShiftRequest {
  storeId: string;
  openingCash?: number;
}

export interface CloseShiftRequest {
  /** Omit (or leave undefined) to close without cash reconciliation — old behavior. */
  actualClosingCash?: number;
}
