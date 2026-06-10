import { api } from "@/lib/api";
import type { TransferDto, CreateTransferRequest } from "../types";

export const transfersApi = {
  getAll: (params?: { store_id?: string; status?: string }) => {
    const qs = new URLSearchParams();
    if (params?.store_id) qs.set("store_id", params.store_id);
    if (params?.status) qs.set("status", params.status);
    const q = qs.toString();
    return api.get<TransferDto[]>(`/api/transfers${q ? `?${q}` : ""}`);
  },

  getById: (id: string) => api.get<TransferDto>(`/api/transfers/${id}`),

  create: (data: CreateTransferRequest) => api.post<TransferDto>("/api/transfers", data),

  confirm: (id: string) => api.put<TransferDto>(`/api/transfers/${id}/confirm`),

  cancel: (id: string) => api.put<TransferDto>(`/api/transfers/${id}/cancel`),
};
