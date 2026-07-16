import { api } from "@/lib/api";
import type {
  ProviderTicketListItemDto,
  ProviderTicketDetailDto,
  CreateProviderTicketPayload,
  ProviderTicketFilters,
  AddCommentPayload,
  TicketCommentDto,
} from "../types";

export const providerTicketsApi = {
  getAll(filters: ProviderTicketFilters = {}): Promise<ProviderTicketListItemDto[]> {
    const params = new URLSearchParams();
    if (filters.status) params.set("status", filters.status);
    if (filters.tenantId) params.set("tenantId", filters.tenantId);
    const qs = params.toString();
    return api.get<ProviderTicketListItemDto[]>(
      `/api/admin/service-desk${qs ? `?${qs}` : ""}`,
    );
  },

  create(data: CreateProviderTicketPayload): Promise<ProviderTicketListItemDto> {
    return api.post<ProviderTicketListItemDto>("/api/admin/service-desk", data);
  },

  getTicket(id: string): Promise<ProviderTicketDetailDto> {
    return api.get<ProviderTicketDetailDto>(`/api/admin/service-desk/${id}`);
  },

  addComment(id: string, data: AddCommentPayload): Promise<TicketCommentDto> {
    return api.post<TicketCommentDto>(`/api/admin/service-desk/${id}/comments`, data);
  },
};
