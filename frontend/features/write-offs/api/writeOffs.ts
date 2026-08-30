import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type { WriteOffDto, CreateWriteOffRequest } from "../types";

export const writeOffsApi = {
  getAll: (params?: {
    store_id?: string;
    status?: string;
    category_id?: string;
    min_loss_amount?: number;
    max_loss_amount?: number;
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
    if (params?.min_loss_amount != null) qs.set("min_loss_amount", String(params.min_loss_amount));
    if (params?.max_loss_amount != null) qs.set("max_loss_amount", String(params.max_loss_amount));
    if (params?.page) qs.set("page", String(params.page));
    if (params?.pageSize) qs.set("pageSize", String(params.pageSize));
    if (params?.search) qs.set("search", params.search);
    if (params?.sortBy) qs.set("sortBy", params.sortBy);
    if (params?.sortDescending !== undefined) qs.set("sortDescending", String(params.sortDescending));
    const q = qs.toString();
    return api.get<PagedResult<WriteOffDto>>(`/api/write-offs${q ? `?${q}` : ""}`);
  },

  getById: (id: string) => api.get<WriteOffDto>(`/api/write-offs/${id}`),

  create: (data: CreateWriteOffRequest) => api.post<WriteOffDto>("/api/write-offs", data),

  approve: (id: string) => api.put<WriteOffDto>(`/api/write-offs/${id}/approve`),

  reject: (id: string) => api.put<WriteOffDto>(`/api/write-offs/${id}/reject`),

  getPdfUrl: (id: string) => `/api/write-offs/${id}/pdf`,
};
