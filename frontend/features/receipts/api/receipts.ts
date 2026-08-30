import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type { ReceiptDto, CreateReceiptRequest, UpdateItemPayload } from "../types";

export const receiptsApi = {
  getAll: (params?: {
    store_id?: string;
    status?: string;
    category_id?: string;
    min_items?: number;
    max_items?: number;
    page?: number;
    pageSize?: number;
    search?: string;
    sortBy?: string;
    sortDescending?: boolean;
  }) => {
    const qs = new URLSearchParams();
    if (params?.store_id) qs.set("store_id", params.store_id);
    if (params?.status) qs.set("status", params.status);
    if (params?.category_id) qs.set("category_id", params.category_id);
    // `!= null` (never a truthy check) — 0 is a valid bound and must not be treated as unset.
    if (params?.min_items != null) qs.set("min_items", String(params.min_items));
    if (params?.max_items != null) qs.set("max_items", String(params.max_items));
    if (params?.page) qs.set("page", String(params.page));
    if (params?.pageSize) qs.set("pageSize", String(params.pageSize));
    if (params?.search) qs.set("search", params.search);
    if (params?.sortBy) qs.set("sortBy", params.sortBy);
    if (params?.sortDescending !== undefined) qs.set("sortDescending", String(params.sortDescending));
    const q = qs.toString();
    return api.get<PagedResult<ReceiptDto>>(`/api/receipts${q ? `?${q}` : ""}`);
  },

  getById: (id: string) => api.get<ReceiptDto>(`/api/receipts/${id}`),

  create: (data: CreateReceiptRequest) => api.post<ReceiptDto>("/api/receipts", data),

  updateItems: (id: string, items: UpdateItemPayload[]) =>
    api.put<ReceiptDto>(`/api/receipts/${id}/items`, { items }),

  receive: (id: string) => api.put<ReceiptDto>(`/api/receipts/${id}/receive`),

  cancel: (id: string) => api.put<ReceiptDto>(`/api/receipts/${id}/cancel`),
};
