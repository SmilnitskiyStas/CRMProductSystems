import { personalApiClient } from '@/lib/api-client';
import type { PagedResult } from '@/features/loyalty/types';
import type { ConsumerSupportMessage, ConsumerSupportTicket } from './types';
export const getMyTickets = async (tenantId: string) => (await personalApiClient.get<PagedResult<ConsumerSupportTicket>>('/consumer/support/tickets', { params: { tenantId } })).data;
export const getTicket = async (id: string) => (await personalApiClient.get<ConsumerSupportTicket>(`/consumer/support/tickets/${id}`)).data;
export const createTicket = async (body: { tenantId: string; subject: string; body: string }) => (await personalApiClient.post<ConsumerSupportTicket>('/consumer/support/tickets', body)).data;
export const addTicketMessage = async (id: string, body: string) => (await personalApiClient.post<ConsumerSupportMessage>(`/consumer/support/tickets/${id}/messages`, { body })).data;
