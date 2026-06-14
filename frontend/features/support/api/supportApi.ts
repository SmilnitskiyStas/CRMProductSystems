import { api } from "@/lib/api";
import type { SupportTicketDto, SupportMessageDto } from "../types";

// ── Client endpoints ──────────────────────────────────────────────────────────

export function getMyTickets(status?: string): Promise<SupportTicketDto[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : "";
  return api.get<SupportTicketDto[]>(`/support/tickets${qs}`);
}

export function getMyTicket(id: string): Promise<SupportTicketDto> {
  return api.get<SupportTicketDto>(`/support/tickets/${id}`);
}

export function createTicket(body: {
  subject: string;
  body: string;
  priority?: string;
}): Promise<SupportTicketDto> {
  return api.post<SupportTicketDto>("/support/tickets", body);
}

export function addClientMessage(ticketId: string, body: string): Promise<SupportMessageDto> {
  return api.post<SupportMessageDto>(`/support/tickets/${ticketId}/messages`, { body });
}

// ── Provider endpoints ────────────────────────────────────────────────────────

export function getAllTickets(params: {
  status?: string;
  assignedTo?: string;
}): Promise<SupportTicketDto[]> {
  const qs = new URLSearchParams();
  if (params.status)     qs.set("status", params.status);
  if (params.assignedTo) qs.set("assignedTo", params.assignedTo);
  const q = qs.toString();
  return api.get<SupportTicketDto[]>(`/provider/support/tickets${q ? `?${q}` : ""}`);
}

export function getProviderTicket(id: string): Promise<SupportTicketDto> {
  return api.get<SupportTicketDto>(`/provider/support/tickets/${id}`);
}

export function assignTicket(ticketId: string, agentId: string): Promise<void> {
  return api.put<void>(`/provider/support/tickets/${ticketId}/assign`, { agentId });
}

export function updateTicketStatus(ticketId: string, status: string): Promise<void> {
  return api.put<void>(`/provider/support/tickets/${ticketId}/status`, { status });
}

export function addProviderMessage(ticketId: string, body: string): Promise<SupportMessageDto> {
  return api.post<SupportMessageDto>(`/provider/support/tickets/${ticketId}/messages`, { body });
}
