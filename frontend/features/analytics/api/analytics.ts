import { api } from "@/lib/api";
import type {
  ExpirySummaryDto,
  WriteOffAnalyticsDto,
  WriteOffAnalyticsCompareDto,
  MovementAnalyticsDto,
  ZoneAnalyticsDto,
  CategoryAnalyticsDto,
  LossesDto,
  LossesCompareDto,
} from "../types";

interface CompareParams {
  compare?: boolean;
  compareFrom?: string;
  compareTo?: string;
}

interface WriteOffParams {
  store_id?: string;
  from?: string;
  to?: string;
}

function buildCompareQs(params: (WriteOffParams & CompareParams) | undefined): string {
  const qs = new URLSearchParams();
  if (params?.store_id) qs.set("store_id", params.store_id);
  if (params?.from) qs.set("from", params.from);
  if (params?.to) qs.set("to", params.to);
  if (params?.compare) qs.set("compare", "true");
  if (params?.compareFrom) qs.set("compareFrom", params.compareFrom);
  if (params?.compareTo) qs.set("compareTo", params.compareTo);
  const q = qs.toString();
  return q ? `?${q}` : "";
}

// Backward compatible: `compare` omitted/false → flat DTO (unchanged shape).
// `compare: true` → wrapped { current, comparison, totalLossPercentChange } DTO.
function getWriteOffs(params?: WriteOffParams & { compare?: false }): Promise<WriteOffAnalyticsDto>;
function getWriteOffs(params: WriteOffParams & { compare: true } & CompareParams): Promise<WriteOffAnalyticsCompareDto>;
function getWriteOffs(
  params?: WriteOffParams & CompareParams,
): Promise<WriteOffAnalyticsDto | WriteOffAnalyticsCompareDto> {
  return api.get(`/api/analytics/write-offs${buildCompareQs(params)}`);
}

function getLosses(params?: WriteOffParams & { compare?: false }): Promise<LossesDto>;
function getLosses(params: WriteOffParams & { compare: true } & CompareParams): Promise<LossesCompareDto>;
function getLosses(params?: WriteOffParams & CompareParams): Promise<LossesDto | LossesCompareDto> {
  return api.get(`/api/analytics/losses${buildCompareQs(params)}`);
}

export const analyticsApi = {
  getExpirySummary: (params?: { store_id?: string; network?: boolean }) => {
    const qs = new URLSearchParams();
    if (params?.store_id) qs.set("store_id", params.store_id);
    if (params?.network) qs.set("network", "true");
    const q = qs.toString();
    return api.get<ExpirySummaryDto>(`/api/analytics/expiry-summary${q ? `?${q}` : ""}`);
  },

  getWriteOffs,

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

  getLosses,
};
