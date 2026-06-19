import { apiClient } from '@/lib/api-client';
import type {
  Ticket,
  TicketDetail,
  TicketComment,
  PagedResult,
  CreateTicketPayload,
  UpdateTicketPayload,
  AddCommentPayload,
} from './types';

export async function getMyTickets(): Promise<Ticket[]> {
  const { data } = await apiClient.get<Ticket[]>('/service-desk/my');
  return data;
}

export async function getTickets(page = 1, status?: string): Promise<PagedResult<Ticket>> {
  const { data } = await apiClient.get<PagedResult<Ticket>>('/service-desk', {
    params: { page, pageSize: 50, status: status || undefined },
  });
  return data;
}

export async function getTicket(id: string): Promise<TicketDetail> {
  const { data } = await apiClient.get<TicketDetail>(`/service-desk/${id}`);
  return data;
}

export async function createTicket(payload: CreateTicketPayload): Promise<Ticket> {
  const { data } = await apiClient.post<Ticket>('/service-desk', payload);
  return data;
}

export async function updateTicket(id: string, payload: UpdateTicketPayload): Promise<Ticket> {
  const { data } = await apiClient.put<Ticket>(`/service-desk/${id}`, payload);
  return data;
}

export async function addComment(
  ticketId: string,
  payload: AddCommentPayload
): Promise<TicketComment> {
  const { data } = await apiClient.post<TicketComment>(
    `/service-desk/${ticketId}/comments`,
    payload
  );
  return data;
}
