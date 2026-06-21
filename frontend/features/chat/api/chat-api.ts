import { api } from "@/lib/api";
import type {
  ChatSessionDto,
  ChatMessageDto,
  CreateChatSessionRequest,
  SendChatMessageRequest,
} from "../types";

// ── Client endpoints (tenant-scoped) ─────────────────────────────────────────

export const chatApi = {
  createSession: (req: CreateChatSessionRequest): Promise<ChatSessionDto> =>
    api.post<ChatSessionDto>("/api/chat/sessions", req),

  getSessions: (): Promise<ChatSessionDto[]> =>
    api.get<ChatSessionDto[]>("/api/chat/sessions"),

  getMessages: (sessionId: string): Promise<ChatMessageDto[]> =>
    api.get<ChatMessageDto[]>(`/api/chat/sessions/${sessionId}/messages`),

  sendMessage: (sessionId: string, req: SendChatMessageRequest): Promise<ChatMessageDto> =>
    api.post<ChatMessageDto>(`/api/chat/sessions/${sessionId}/messages`, req),

  closeSession: (sessionId: string): Promise<void> =>
    api.post<void>(`/api/chat/sessions/${sessionId}/close`),
};

// ── Provider endpoints (cross-tenant) ────────────────────────────────────────

export const providerChatApi = {
  getAllSessions: (): Promise<ChatSessionDto[]> =>
    api.get<ChatSessionDto[]>("/api/admin/chat/sessions"),

  getMessages: (sessionId: string): Promise<ChatMessageDto[]> =>
    api.get<ChatMessageDto[]>(`/api/admin/chat/sessions/${sessionId}/messages`),

  sendMessage: (sessionId: string, req: SendChatMessageRequest): Promise<ChatMessageDto> =>
    api.post<ChatMessageDto>(`/api/admin/chat/sessions/${sessionId}/messages`, req),

  closeSession: (sessionId: string): Promise<void> =>
    api.post<void>(`/api/admin/chat/sessions/${sessionId}/close`),
};
