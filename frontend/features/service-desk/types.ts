export type TicketStatus = "open" | "in_progress" | "waiting" | "resolved" | "closed";
export type TicketPriority = "low" | "medium" | "high" | "critical";
export type TicketCategory =
  | "general"
  | "technical"
  | "billing"
  | "feature_request"
  | "bug";

export interface TicketDto {
  id: string;
  number: number;
  title: string;
  description: string;
  category: TicketCategory;
  priority: TicketPriority;
  status: TicketStatus;
  createdBy: string;
  createdByName: string;
  assignedTo?: string;
  assignedToName?: string;
  locationId?: string;
  locationName?: string;
  resolvedAt?: string;
  createdAt: string;
  commentCount: number;
}

export interface TicketCommentDto {
  id: string;
  authorId: string;
  authorName: string;
  body: string;
  isInternal: boolean;
  createdAt: string;
}

export interface TicketDetailDto extends TicketDto {
  comments: TicketCommentDto[];
}

export interface TicketsPage {
  items: TicketDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface TicketFilters {
  status?: TicketStatus | "";
  priority?: TicketPriority | "";
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface CreateTicketPayload {
  title: string;
  description: string;
  category: TicketCategory;
  priority: TicketPriority;
  locationId?: string;
}

export interface UpdateTicketPayload {
  title?: string;
  description?: string;
  category?: TicketCategory;
  priority?: TicketPriority;
  status?: TicketStatus;
  assignedTo?: string;
}

export interface AddCommentPayload {
  body: string;
  isInternal: boolean;
}

// ── Provider cross-tenant types (TASK-271) ──────────────────────────────────

export interface ProviderTicketListItemDto {
  id: string;
  number: number;
  tenantId: string;
  tenantName: string;
  title: string;
  description: string;
  category: TicketCategory;
  priority: TicketPriority;
  status: TicketStatus;
  createdBy: string;
  createdByName: string;
  createdByProvider: boolean;
  createdAt: string;
  commentCount: number;
}

export interface CreateProviderTicketPayload {
  targetTenantId: string;
  title: string;
  description: string;
  category: TicketCategory;
  priority: TicketPriority;
}

export interface ProviderTicketDetailDto {
  id: string;
  number: number;
  tenantId: string;
  tenantName: string;
  title: string;
  description: string;
  category: TicketCategory;
  priority: TicketPriority;
  status: TicketStatus;
  createdBy: string;
  createdByName: string;
  createdByProvider: boolean;
  createdAt: string;
  comments: TicketCommentDto[];
}

export interface ProviderTicketFilters {
  status?: TicketStatus | "";
  tenantId?: string;
}

export const TICKET_STATUS_LABELS: Record<TicketStatus, string> = {
  open: "Відкритий",
  in_progress: "В роботі",
  waiting: "Очікування",
  resolved: "Вирішено",
  closed: "Закрито",
};

export const TICKET_PRIORITY_LABELS: Record<TicketPriority, string> = {
  low: "Низький",
  medium: "Середній",
  high: "Високий",
  critical: "Критичний",
};

export const TICKET_CATEGORY_LABELS: Record<TicketCategory, string> = {
  general: "Загальне",
  technical: "Технічне",
  billing: "Оплата",
  feature_request: "Запит функції",
  bug: "Помилка",
};
