import { api } from "@/lib/api";
import type {
  ExpirySummaryDto,
  WriteOffAnalyticsDto,
  MovementAnalyticsDto,
  ZoneAnalyticsDto,
  CategoryAnalyticsDto,
  LossesDto,
} from "../types";

export const analyticsApi = {
  getExpirySummary: (params?: { store_id?: string; network?: boolean }) => {
    const qs = new URLSearchParams();
    if (params?.store_id) qs.set("store_id", params.store_id);
    if (params?.network) qs.set("network", "true");
    const q = qs.toString();
    return api.get<ExpirySummaryDto>(`/api/analytics/expiry-summary${q ? `?${q}` : ""}`);
  },

  getWriteOffs: (params?: { store_id?: string; from?: string; to?: string }) => {
    const qs = new URLSearchParams();
    if (params?.store_id) qs.set("store_id", params.store_id);
    if (params?.from) qs.set("from", params.from);
    if (params?.to) qs.set("to", params.to);
    const q = qs.toString();
    return api.get<WriteOffAnalyticsDto>(`/api/analytics/write-offs${q ? `?${q}` : ""}`);
  },

  getMovements: (params?: { store_id?: string; type?: string; from?: string; to?: string }) => {
    const qs = new URLSearchParams();
    if (params?.store_id) qs.set("store_id", params.store_id);
    if (params?.type) qs.set("type", params.type);
    if (params?.from) qs.set("from", params.from);
    if (params?.to) qs.set("to", params.to);
    const q = qs.toString();
    return api.get<MovementAnalyticsDto>(`/api/analytics/movements${q ? `?${q}` : ""}`);
  },

  getByZone: (store_id?: string) => {
    const qs = store_id ? `?store_id=${store_id}` : "";
    return api.get<ZoneAnalyticsDto[]>(`/api/analytics/by-zone${qs}`);
  },

  getByCategory: (store_id?: string) => {
    const qs = store_id ? `?store_id=${store_id}` : "";
    return api.get<CategoryAnalyticsDto[]>(`/api/analytics/by-category${qs}`);
  },

  getLosses: (params?: { store_id?: string; from?: string; to?: string }) => {
    const qs = new URLSearchParams();
    if (params?.store_id) qs.set("store_id", params.store_id);
    if (params?.from) qs.set("from", params.from);
    if (params?.to) qs.set("to", params.to);
    const q = qs.toString();
    return api.get<LossesDto>(`/api/analytics/losses${q ? `?${q}` : ""}`);
  },
};
