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
  CategoryProductBreakdownDto,
  LossesByProductDto,
  LossesTrendDto,
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

// ── Category × product breakdown / losses × product (TASK-483) ─────────────

interface CategoryProductBreakdownParams {
  /** null = the "uncategorized" bucket (matches CategoryProductBreakdownDto's own convention —
   * omitted from the querystring is how the backend's `Guid? category_id` reads "uncategorized",
   * same as leaving it out entirely). Only a real id is ever put on the wire. */
  category_id?: string | null;
  store_id?: string;
  from?: string;
  to?: string;
}

interface LossesByProductParams {
  store_id?: string;
  reason?: string;
  from?: string;
  to?: string;
}

function getCategoryProductBreakdown(params?: CategoryProductBreakdownParams): Promise<CategoryProductBreakdownDto> {
  const qs = new URLSearchParams();
  if (params?.category_id) qs.set("category_id", params.category_id);
  if (params?.store_id) qs.set("store_id", params.store_id);
  if (params?.from) qs.set("from", params.from);
  if (params?.to) qs.set("to", params.to);
  const q = qs.toString();
  return api.get(`/api/analytics/by-category/products${q ? `?${q}` : ""}`);
}

function getLossesByProduct(params?: LossesByProductParams): Promise<LossesByProductDto> {
  const qs = new URLSearchParams();
  if (params?.store_id) qs.set("store_id", params.store_id);
  if (params?.reason) qs.set("reason", params.reason);
  if (params?.from) qs.set("from", params.from);
  if (params?.to) qs.set("to", params.to);
  const q = qs.toString();
  return api.get(`/api/analytics/losses/by-product${q ? `?${q}` : ""}`);
}

// ── Losses/write-offs trend over time (TASK-489/492) ────────────────────────

interface LossesTrendParams {
  store_id?: string;
  from?: string;
  to?: string;
  group_by?: "day" | "week";
}

function getLossesTrend(params?: LossesTrendParams): Promise<LossesTrendDto> {
  const qs = new URLSearchParams();
  if (params?.store_id) qs.set("store_id", params.store_id);
  if (params?.from) qs.set("from", params.from);
  if (params?.to) qs.set("to", params.to);
  if (params?.group_by) qs.set("group_by", params.group_by);
  const q = qs.toString();
  return api.get(`/api/analytics/losses/trend${q ? `?${q}` : ""}`);
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

  getCategoryProductBreakdown,
  getLossesByProduct,
  getLossesTrend,
};
