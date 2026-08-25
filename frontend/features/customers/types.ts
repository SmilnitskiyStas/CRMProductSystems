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
