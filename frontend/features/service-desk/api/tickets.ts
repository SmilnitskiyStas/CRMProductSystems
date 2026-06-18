import { api } from "@/lib/api";
import type {
  TicketDto,
  TicketDetailDto,
  TicketsPage,
  TicketFilters,
  CreateTicketPayload,
  UpdateTicketPayload,
  AddCommentPayload,
  TicketCommentDto,
} from "../types";

export const ticketsApi = {
  getTickets(filters: TicketFilters = {}): Promise<TicketsPage> {
    const params = new URLSearchParams();
    params.set("page", String(filters.page ?? 1));
    params.set("pageSize", String(filters.pageSize ?? 20));
    if (filters.status) params.set("status", filters.status);
    if (filters.priority) params.set("priority", filters.priority);
    if (filters.search) params.set("search", filters.search);
    return api.get<TicketsPage>(`/api/service-desk?${params.toString()}`);
  },

  getMyTickets(): Promise<TicketDto[]> {
    return api.get<TicketDto[]>("/api/service-desk/my");
  },

  getTicket(id: string): Promise<TicketDetailDto> {
    return api.get<TicketDetailDto>(`/api/service-desk/${id}`);
  },

  createTicket(data: CreateTicketPayload): Promise<TicketDto> {
    return api.post<TicketDto>("/api/service-desk", data);
  },

  updateTicket(id: string, data: UpdateTicketPayload): Promise<TicketDto> {
    return api.put<TicketDto>(`/api/service-desk/${id}`, data);
  },

  addComment(id: string, data: AddCommentPayload): Promise<TicketCommentDto> {
    return api.post<TicketCommentDto>(`/api/service-desk/${id}/comments`, data);
  },
};
