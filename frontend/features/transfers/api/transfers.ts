import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type { TransferDto, CreateTransferRequest } from "../types";

export const transfersApi = {
  getAll: (params?: {
    store_id?: string;
    status?: string;
    page?: number;
    pageSize?: number;
    search?: string;
    sortBy?: string;
    sortDescending?: boolean;
  }) => {
    const qs = new URLSearchParams();
    if (params?.store_id) qs.set("store_id", params.store_id);
    if (params?.status) qs.set("status", params.status);
    if (params?.page) qs.set("page", String(params.page));
    if (params?.pageSize) qs.set("pageSize", String(params.pageSize));
    if (params?.search) qs.set("search", params.search);
    if (params?.sortBy) qs.set("sortBy", params.sortBy);
    if (params?.sortDescending !== undefined) qs.set("sortDescending", String(params.sortDescending));
    const q = qs.toString();
    return api.get<PagedResult<TransferDto>>(`/api/transfers${q ? `?${q}` : ""}`);
  },

  getById: (id: string) => api.get<TransferDto>(`/api/transfers/${id}`),

  create: (data: CreateTransferRequest) => api.post<TransferDto>("/api/transfers", data),

  confirm: (id: string) => api.put<TransferDto>(`/api/transfers/${id}/confirm`),

  cancel: (id: string) => api.put<TransferDto>(`/api/transfers/${id}/cancel`),
};
