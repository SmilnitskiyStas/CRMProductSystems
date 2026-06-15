export type TicketStatus   = "open" | "in_progress" | "resolved" | "closed";
export type TicketPriority = "low" | "normal" | "high" | "urgent";

export interface SupportMessageDto {
  id: string;
  senderId: string;
  isProviderMessage: boolean;
  body: string;
  createdAt: string;
}

export interface SupportTicketDto {
  id: string;
  tenantId: string;
  createdBy: string;
  assignedTo: string | null;
  subject: string;
  status: TicketStatus;
  priority: TicketPriority;
  createdAt: string;
  updatedAt: string;
  isUnread: boolean;
  messages: SupportMessageDto[];
}

export const STATUS_LABELS: Record<TicketStatus, string> = {
  open:        "Відкритий",
  in_progress: "В роботі",
  resolved:    "Вирішений",
  closed:      "Закритий",
};

export const PRIORITY_LABELS: Record<TicketPriority, string> = {
  low:    "Низький",
  normal: "Нормальний",
  high:   "Високий",
  urgent: "Терміново",
};

export const STATUS_COLORS: Record<TicketStatus, string> = {
  open:        "bg-blue-500/20 text-blue-300 border-blue-500/30",
  in_progress: "bg-yellow-500/20 text-yellow-300 border-yellow-500/30",
  resolved:    "bg-green-500/20 text-green-300 border-green-500/30",
  closed:      "bg-gray-500/20 text-gray-400 border-gray-500/30",
};

export const PRIORITY_COLORS: Record<TicketPriority, string> = {
  low:    "bg-gray-500/20 text-gray-400 border-gray-500/30",
  normal: "bg-blue-500/20 text-blue-300 border-blue-500/30",
  high:   "bg-orange-500/20 text-orange-300 border-orange-500/30",
  urgent: "bg-red-500/20 text-red-300 border-red-500/30",
};
