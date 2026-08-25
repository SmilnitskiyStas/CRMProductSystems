import { api } from "@/lib/api";
import type {
  ConsumerSupportTicketDto,
  TicketsPage,
  TicketFilters,
} from "../types";

export const customerSupportTicketsApi = {
  // GetInboxAsync (TASK-616) only filters by status, not customerId — see IConsumerSupportService.
  // A `?customerId=` deep link from the customer drawer is handled client-side by the page.
  getTickets(filters: TicketFilters = {}): Promise<TicketsPage> {
    const params = new URLSearchParams();
    params.set("page", String(filters.page ?? 1));
    params.set("pageSize", String(filters.pageSize ?? 50));
    if (filters.status) params.set("status", filters.status);
    return api.get<TicketsPage>(`/api/customer-support/tickets?${params.toString()}`);
  },

  getTicket(id: string): Promise<ConsumerSupportTicketDto> {
    return api.get<ConsumerSupportTicketDto>(`/api/customer-support/tickets/${id}`);
  },

  reply(id: string, body: string) {
    return api.post(`/api/customer-support/tickets/${id}/reply`, { body });
  },

  updateStatus(id: string, status: string): Promise<ConsumerSupportTicketDto> {
    return api.put<ConsumerSupportTicketDto>(`/api/customer-support/tickets/${id}/status`, { status });
  },
};
