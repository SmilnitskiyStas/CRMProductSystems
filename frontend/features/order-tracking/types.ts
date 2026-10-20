export type TrackedOrderStatus = "new" | "confirmed" | "in_transit" | "delivered" | "cancelled";
export type TrackedOrderSource = "receipt" | "marketplace";

export interface OrderTimelineStep {
  /** created | confirmed | shipped | delivered | cancelled */
  key: string;
  at: string | null;
  by: string | null;
}

export interface TrackedOrderLine {
  name: string;
  quantity: number;
  quantityReceived: number | null;
  unit: string | null;
  amount: number | null;
}

export interface TrackedOrder {
  source: TrackedOrderSource;
  id: string;
  number: string | null;
  status: TrackedOrderStatus;
  counterparty: string | null;
  storeId: string | null;
  storeName: string | null;
  createdAt: string;
  expectedAt: string | null;
  isOverdue: boolean;
  daysOverdue: number | null;
  totalQuantity: number;
  totalAmount: number;
  note: string | null;
  timeline: OrderTimelineStep[];
  lines: TrackedOrderLine[];
  /** How many times the expected date was moved (internal orders only). */
  postponeCount: number;
  originalExpectedAt: string | null;
  postponedAt: string | null;
  /** True for open internal orders — a marketplace date is set by the supplier. */
  canPostpone: boolean;
}

export interface OrderTrackingPage {
  items: TrackedOrder[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface OrderTrackingSummary {
  new: number;
  confirmed: number;
  inTransit: number;
  delivered: number;
  cancelled: number;
  overdue: number;
  open: number;
  openQuantity: number;
  inTransitQuantity: number;
  overdueQuantity: number;
  postponed: number;
}

export interface OrderTrackingFilters {
  source?: TrackedOrderSource;
  statuses: TrackedOrderStatus[];
  storeId?: string;
  search?: string;
  /** Only open orders already past their expected delivery date. */
  overdue?: boolean;
  page: number;
  pageSize: number;
}
