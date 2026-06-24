export interface ChatSessionDto {
  id: string;
  tenantId: string;
  tenantName: string;
  subject: string;
  status: "open" | "closed";
  unreadCount: number;
  createdAt: string;
  updatedAt: string;
  closedAt: string | null;
  rating: number | null;
  ratingComment: string | null;
  assignedAgentName: string | null;
  lastMessage: ChatMessageDto | null;
}

export interface ChatMessageDto {
  id: string;
  sessionId: string;
  senderUserId: string;
  senderName: string;
  body: string;
  isRead: boolean;
  isSystem: boolean;
  createdAt: string;
}

export interface CreateChatSessionRequest {
  subject: string;
  firstMessage: string;
}

export interface SendChatMessageRequest {
  body: string;
}

export interface SubmitRatingRequest {
  rating: number;
  comment?: string;
}
