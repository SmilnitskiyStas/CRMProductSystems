// ─── Shift ───────────────────────────────────────────────────────────────────

export type ShiftStatus =
  | 'Opening'
  | 'Open'
  | 'OpenFailed'
  | 'Closing'
  | 'Closed'
  | 'CloseFailed';
export type FiscalStatus =
  | 'fiscalized'
  | 'pending_fiscalization'
  | 'not_required'
  | 'error';

export interface PosShift {
  shiftId: string;
  status: ShiftStatus;
  openedAt?: string;
  closedAt?: string;
  fiscalStatus: FiscalStatus;
}

// ─── Sale / Cart ──────────────────────────────────────────────────────────────

export type PaymentType = 'Cash' | 'Card';

export interface SaleItem {
  barcode: string;
  quantity: number;
  /** Enriched locally after barcode lookup */
  productName?: string;
  unitPrice?: number;
  isCritical?: boolean;
}

export interface SaleRequest {
  shiftId: string;
  items: Array<{ barcode: string; quantity: number }>;
  paymentType: PaymentType;
  paymentAmount: number;
  /** TASK-405/407 (Loyalty): CRM customer to attribute this sale to. Omit for a plain sale. */
  customerId?: string;
  /** Loyalty membership to accrue bonus on (and redeem from, with redeemAmount). */
  loyaltyMembershipId?: string;
  /** Bonus amount to redeem against this sale. Requires loyaltyMembershipId. */
  redeemAmount?: number;
}

export interface SaleResponseItem {
  barcode: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface SaleResponse {
  transactionId: string;
  items: SaleResponseItem[];
  subtotal: number;
  paymentType: PaymentType;
  paymentAmount: number;
  change: number;
  fiscalStatus: FiscalStatus;
  fiscalNumber?: string;
  // TASK-405/407 (Loyalty): undefined when the sale carried no loyaltyMembershipId.
  // loyaltyBalance is the membership's balance AFTER this sale's accrual/redemption.
  loyaltyAccrued?: number;
  loyaltyRedeemed?: number;
  loyaltyBalance?: number;
}

// ─── Sales list ───────────────────────────────────────────────────────────────

export interface SaleListItem {
  transactionId: string;
  subtotal: number;
  paymentType: PaymentType;
  fiscalStatus: FiscalStatus;
  createdAt: string;
}
