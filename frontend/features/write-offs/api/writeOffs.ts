import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type { WriteOffDto, CreateWriteOffRequest } from "../types";

export const writeOffsApi = {
  getAll: (params?: { store_id?: string; status?: string; page?: number; pageSize?: number }) => {
    const qs = new URLSearchParams();
    if (params?.store_id) qs.set("store_id", params.store_id);
    if (params?.status) qs.set("status", params.status);
    if (params?.page) qs.set("page", String(params.page));
    if (params?.pageSize) qs.set("pageSize", String(params.pageSize));
    const q = qs.toString();
    return api.get<PagedResult<WriteOffDto>>(`/api/write-offs${q ? `?${q}` : ""}`);
  },

  getById: (id: string) => api.get<WriteOffDto>(`/api/write-offs/${id}`),

  create: (data: CreateWriteOffRequest) => api.post<WriteOffDto>("/api/write-offs", data),

  approve: (id: string) => api.put<WriteOffDto>(`/api/write-offs/${id}/approve`),

  reject: (id: string) => api.put<WriteOffDto>(`/api/write-offs/${id}/reject`),

  getPdfUrl: (id: string) => `/api/write-offs/${id}/pdf`,
};
