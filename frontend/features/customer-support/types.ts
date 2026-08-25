// TASK-621 (§4 "Вхідні звернень і відгуків"). Contracts mirror the backend DTOs exactly —
// see .claude/logs/tasks/616_..._backend-developer.md (ConsumerSupportTicketDto) and
// .claude/logs/tasks/617_..._backend-developer.md (PurchaseReviewDto). No shared feature with
// `frontend/features/service-desk/` (that one is tenant↔provider tickets, a different domain) —
// this file structurally mirrors its conventions per the task brief, not its types.

export type TicketStatus = "open" | "in_progress" | "resolved" | "closed";

export const TICKET_STATUSES: TicketStatus[] = ["open", "in_progress", "resolved", "closed"];

const TICKET_STATUS_I18N_KEY: Record<TicketStatus, string> = {
  open: "open",
  in_progress: "inProgress",
  resolved: "resolved",
  closed: "closed",
};

/** Translated ticket-status label. `t` must be scoped to `Dashboard.customerSupport.statuses`. */
export function getTicketStatusLabel(t: (key: string) => string, status: TicketStatus): string {
  return t(TICKET_STATUS_I18N_KEY[status] ?? status);
}

export interface ConsumerSupportTicketMessageDto {
  id: string;
  ticketId: string;
  /** Exactly one of senderConsumerAccountId/senderUserId is set — "mine vs theirs" for the UI. */
  senderConsumerAccountId: string | null;
  senderUserId: string | null;
  body: string;
  isRead: boolean;
  createdAt: string;
}

export interface ConsumerSupportTicketDto {
  id: string;
  tenantId: string;
  consumerAccountId: string;
  consumerName: string;
  consumerPhone: string;
  customerId: string | null;
  customerName: string | null;
  subject: string;
  status: TicketStatus;
  createdAt: string;
  updatedAt: string;
  /** null on the list endpoint; populated (oldest-first) only on the single-ticket read. */
  messages: ConsumerSupportTicketMessageDto[] | null;
}

export interface TicketsPage {
  items: ConsumerSupportTicketDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface TicketFilters {
  status?: TicketStatus | "";
  page?: number;
  pageSize?: number;
}

export interface PurchaseReviewDto {
  id: string;
  tenantId: string;
  consumerAccountId: string;
  consumerName: string;
  consumerPhone: string;
  posTransactionId: string;
  rating: number;
  comment: string | null;
  createdAt: string;
  replyText: string | null;
  repliedAt: string | null;
  repliedByUserId: string | null;
}

export interface ReviewsPage {
  items: PurchaseReviewDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface ReviewFilters {
  rating?: number;
  page?: number;
  pageSize?: number;
}
