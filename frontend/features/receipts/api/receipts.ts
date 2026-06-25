import { api } from "@/lib/api";
import type { ReceiptDto, CreateReceiptRequest, UpdateItemPayload } from "../types";

interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export const receiptsApi = {
  getAll: (params?: { store_id?: string; status?: string }) => {
    const qs = new URLSearchParams();
    if (params?.store_id) qs.set("store_id", params.store_id);
    if (params?.status) qs.set("status", params.status);
    const q = qs.toString();
    return api
      .get<PagedResult<ReceiptDto>>(`/api/receipts${q ? `?${q}` : ""}`)
      .then((r) => r.items);
  },

  getById: (id: string) => api.get<ReceiptDto>(`/api/receipts/${id}`),

  create: (data: CreateReceiptRequest) => api.post<ReceiptDto>("/api/receipts", data),

  updateItems: (id: string, items: UpdateItemPayload[]) =>
    api.put<ReceiptDto>(`/api/receipts/${id}/items`, { items }),

  receive: (id: string) => api.put<ReceiptDto>(`/api/receipts/${id}/receive`),

  cancel: (id: string) => api.put<ReceiptDto>(`/api/receipts/${id}/cancel`),
};
