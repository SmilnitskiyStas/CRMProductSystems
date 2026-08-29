import { api } from "@/lib/api";
import type {
  BannerAnalyticsDto,
  BannerDto,
  CreateBannerRequest,
  UpdateBannerRequest,
} from "../types";

export const bannersApi = {
  getAll: (params?: { locationId?: string; activeOnly?: boolean }) => {
    const qs = new URLSearchParams();
    if (params?.locationId) qs.set("locationId", params.locationId);
    if (params?.activeOnly !== undefined) qs.set("activeOnly", String(params.activeOnly));
    const q = qs.toString();
    return api.get<BannerDto[]>(`/api/banners${q ? `?${q}` : ""}`);
  },

  getById: (id: string) => api.get<BannerDto>(`/api/banners/${id}`),

  create: (body: CreateBannerRequest) => api.post<BannerDto>("/api/banners", body),

  update: (id: string, body: UpdateBannerRequest) => api.put<BannerDto>(`/api/banners/${id}`, body),

  // TASK-525: idempotent first-publish — sets publishedAt, moves lifecycleStatus draft → running.
  publish: (id: string) => api.post<BannerDto>(`/api/banners/${id}/publish`),

  // Soft-hide (IsActive=false server-side) — never a hard delete.
  deactivate: (id: string) => api.delete<void>(`/api/banners/${id}`),

  uploadImage: (id: string, file: File) => {
    const form = new FormData();
    form.append("file", file);
    return api.postForm<{ imageUrl: string }>(`/api/banners/${id}/image`, form);
  },

  getAnalytics: (id: string, params?: { from?: string; to?: string; storeIds?: string[] }) => {
    const qs = new URLSearchParams();
    if (params?.from) qs.set("from", params.from);
    if (params?.to) qs.set("to", params.to);
    params?.storeIds?.forEach((storeId) => qs.append("storeIds", storeId));
    return api.get<BannerAnalyticsDto>(`/api/banners/${id}/analytics${qs.size ? `?${qs}` : ""}`);
  },
};
