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
  /**
   * TASK-405 (backend, Loyalty Фаза 0): bonus amount accrued on this sale. Only set when the
   * sale carried a LoyaltyMembershipId at creation.
   *
   * TASK-408 finding: these three fields are populated ONLY in the direct response of
   * `POST /api/pos/sales` (mobile checkout — `PosService.CreateSaleAsync`,
   * backend/ShelfGuard.Application/Features/Pos/PosService.cs:534-548). The web dashboard
   * never calls that endpoint — it only reads `GET /api/pos/sales` (`GetSalesForShiftAsync`,
   * same file, line ~682), whose SaleDto mapping omits Loyalty* entirely, so these are always
   * null for every sale rendered here today. Kept as real (nullable) fields rather than
   * invented ones — see SalesTable.tsx / SaleDetailDrawer.tsx for the read-only UI gated on
   * them, and .claude/logs/tasks/408_2026-07-26_web-pos-loyalty-section_frontend-developer.md
   * for the full backend-gap writeup.
   */
  loyaltyAccrued: number | null;
  /** Bonus amount redeemed against this sale. See loyaltyAccrued doc above for availability. */
  loyaltyRedeemed: number | null;
  /** Membership balance after this sale's accrual/redemption. See loyaltyAccrued doc above. */
  loyaltyBalance: number | null;
}

/**
 * True when the backend returned any loyalty ledger amount for this sale. Centralised here
 * (rather than duplicated per-component) so SalesTable's indicator and SaleDetailDrawer's
 * section always agree on what "has loyalty activity" means. See loyaltyAccrued doc on
 * SaleDto for why this is always false via GET /api/pos/sales today (TASK-408 finding).
 */
export function saleHasLoyaltyActivity(
  sale: Pick<SaleDto, "loyaltyAccrued" | "loyaltyRedeemed" | "loyaltyBalance">,
): boolean {
  return sale.loyaltyAccrued != null || sale.loyaltyRedeemed != null || sale.loyaltyBalance != null;
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
