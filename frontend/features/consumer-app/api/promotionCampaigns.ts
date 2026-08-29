import { api } from "@/lib/api";
import type { PromotionCampaignAnalyticsDto, PromotionCampaignDto, UpsertPromotionCampaignRequest } from "../types";

export const promotionCampaignsApi = {
  getAll: () => api.get<PromotionCampaignDto[]>("/api/promotion-campaigns"),
  getById: (id: string) => api.get<PromotionCampaignDto>(`/api/promotion-campaigns/${id}`),
  create: (body: UpsertPromotionCampaignRequest) => api.post<PromotionCampaignDto>("/api/promotion-campaigns", body),
  update: (id: string, body: UpsertPromotionCampaignRequest) => api.put<PromotionCampaignDto>(`/api/promotion-campaigns/${id}`, body),
  publish: (id: string) => api.post<PromotionCampaignDto>(`/api/promotion-campaigns/${id}/publish`, {}),
  cancel: (id: string) => api.delete(`/api/promotion-campaigns/${id}`),
  uploadImage: async (id: string, file: File) => { const data = new FormData(); data.append("file", file); return api.postForm<{ imageUrl: string }>(`/api/promotion-campaigns/${id}/image`, data); },
  analytics: (id: string, params: { from: string; to: string; storeIds: string[] }) => {
    const qs = new URLSearchParams({ from: params.from, to: params.to });
    params.storeIds.forEach((storeId) => qs.append("storeIds", storeId));
    return api.get<PromotionCampaignAnalyticsDto>(`/api/promotion-campaigns/${id}/analytics?${qs}`);
  },
};
