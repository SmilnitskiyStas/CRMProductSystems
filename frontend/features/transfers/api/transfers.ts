import { api } from "@/lib/api";
import type { TransferDto, CreateTransferRequest } from "../types";

interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export const transfersApi = {
  getAll: (params?: { store_id?: string; status?: string }) => {
    const qs = new URLSearchParams();
    if (params?.store_id) qs.set("store_id", params.store_id);
    if (params?.status) qs.set("status", params.status);
    const q = qs.toString();
    return api
      .get<PagedResult<TransferDto>>(`/api/transfers${q ? `?${q}` : ""}`)
      .then((r) => r.items);
  },

  getById: (id: string) => api.get<TransferDto>(`/api/transfers/${id}`),

  create: (data: CreateTransferRequest) => api.post<TransferDto>("/api/transfers", data),

  confirm: (id: string) => api.put<TransferDto>(`/api/transfers/${id}/confirm`),

  cancel: (id: string) => api.put<TransferDto>(`/api/transfers/${id}/cancel`),
};
