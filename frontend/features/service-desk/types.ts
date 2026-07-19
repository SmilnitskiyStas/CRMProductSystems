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

/**
 * Status/priority/category labels moved to i18n as of i18n Block 10 (TASK-389):
 * `Dashboard.serviceDesk.statuses.*` / `.priorities.*` / `.categories.*`. Key-order arrays
 * kept here as the canonical iteration order for selects/filters (service-desk components
 * *and* `features/provider/components/ProviderSupportTab.tsx`, which renders the same
 * ticket data cross-tenant); label lookup goes through the getX Label helpers below,
 * mirroring `getRoleLabel` (features/profile/types.ts) and `getEventTypeLabel`
 * (features/notifications/types.ts).
 */
export const TICKET_STATUSES: TicketStatus[] = ["open", "in_progress", "waiting", "resolved", "closed"];
export const TICKET_PRIORITIES: TicketPriority[] = ["low", "medium", "high", "critical"];
export const TICKET_CATEGORIES: TicketCategory[] = ["general", "technical", "billing", "feature_request", "bug"];

const TICKET_STATUS_I18N_KEY: Record<TicketStatus, string> = {
  open: "open",
  in_progress: "inProgress",
  waiting: "waiting",
  resolved: "resolved",
  closed: "closed",
};

const TICKET_PRIORITY_I18N_KEY: Record<TicketPriority, string> = {
  low: "low",
  medium: "medium",
  high: "high",
  critical: "critical",
};

const TICKET_CATEGORY_I18N_KEY: Record<TicketCategory, string> = {
  general: "general",
  technical: "technical",
  billing: "billing",
  feature_request: "featureRequest",
  bug: "bug",
};

/** Translated ticket-status label. `t` must be scoped to `Dashboard.serviceDesk.statuses`. */
export function getTicketStatusLabel(t: (key: string) => string, status: TicketStatus): string {
  return t(TICKET_STATUS_I18N_KEY[status] ?? status);
}

/** Translated ticket-priority label. `t` must be scoped to `Dashboard.serviceDesk.priorities`. */
export function getTicketPriorityLabel(t: (key: string) => string, priority: TicketPriority): string {
  return t(TICKET_PRIORITY_I18N_KEY[priority] ?? priority);
}

/** Translated ticket-category label. `t` must be scoped to `Dashboard.serviceDesk.categories`. */
export function getTicketCategoryLabel(t: (key: string) => string, category: TicketCategory): string {
  return t(TICKET_CATEGORY_I18N_KEY[category] ?? category);
}
