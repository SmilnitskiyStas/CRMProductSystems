export interface Customer {
  id: string;
  name: string;
  phone?: string;
  email?: string;
  notes?: string;
  tags: string[];
  totalOrders: number;
  totalSpent: number;
  createdAt: string;
}

export interface CustomerDetail extends Customer {
  recentTransactions: CustomerTransaction[];
  // TASK-618: loyalty tier + support/reviews summary, added for free to the existing
  // GET /api/customers/{id} response — see .claude/logs/handoffs/618-to-frontend_backend-developer.md
  // for the three-state null semantics (not enrolled / enrolled no tier yet / top tier).
  currentTierName: string | null;
  compositeScore: number | null;
  tierProgressPercent: number | null;
  openTicketCount: number;
  recentReviews: CustomerReviewPreview[];
}

export interface CustomerTransaction {
  id: string;
  totalAmount: number;
  paymentType: string;
  createdAt: string;
  status: string;
}

/** One entry of CustomerDetail.recentReviews — capped at 5, newest-first, never null. */
export interface CustomerReviewPreview {
  rating: number;
  comment: string | null;
  createdAt: string;
  replyText: string | null;
}

/**
 * TASK-621b: GET /api/customers/{id}/profile-history row. fieldName is one of
 * "full_name" | "email" | "phone" (ConsumerAccountProfileChangeField) — translated in the UI.
 */
export interface ConsumerProfileChange {
  fieldName: string;
  oldValue: string | null;
  newValue: string | null;
  changedAt: string;
}

export interface ConsumerProfileChangePage {
  items: ConsumerProfileChange[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface CustomersPage {
  items: Customer[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface CreateCustomerPayload {
  name: string;
  phone?: string;
  email?: string;
  notes?: string;
  tags?: string[];
}

export interface UpdateCustomerPayload {
  name: string;
  phone?: string;
  email?: string;
  notes?: string;
  tags?: string[];
}

// ── Customer profile page: purchase analytics ──────────────────────────────────────────────────

export interface CustomerActivityStats {
  receiptCount: number;
  totalSpent: number;
  averageCheck: number;
  itemsQuantity: number;
  firstPurchaseAt: string | null;
  lastPurchaseAt: string | null;
  daysSinceLastPurchase: number | null;
  daysSinceRegistered: number;
  averageDaysBetweenPurchases: number | null;
  receipts30d: number;
  spent30d: number;
  receipts90d: number;
  spent90d: number;
}

export interface CustomerStoreActivity {
  storeId: string;
  storeName: string;
  receipts: number;
  spent: number;
  sharePercent: number;
  firstAt: string;
  lastAt: string;
}

export interface CustomerMonthActivity {
  /** "YYYY-MM" */
  month: string;
  receipts: number;
  spent: number;
}

export interface CustomerTopProduct {
  productId: string;
  name: string;
  quantity: number;
  spent: number;
  receipts: number;
  lastAt: string;
}

export interface CustomerActivity {
  stats: CustomerActivityStats;
  stores: CustomerStoreActivity[];
  months: CustomerMonthActivity[];
  topProducts: CustomerTopProduct[];
}

export interface CustomerReceiptLine {
  productId: string;
  name: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  total: number;
}

export interface CustomerReceipt {
  id: string;
  receiptNumber: string;
  fiscalNumber: string | null;
  createdAt: string;
  storeId: string;
  storeName: string;
  total: number;
  paymentType: string;
  status: string;
  lines: CustomerReceiptLine[];
}

export interface CustomerReceiptsPage {
  items: CustomerReceipt[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
