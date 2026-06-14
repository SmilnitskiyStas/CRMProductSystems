import { api } from "@/lib/api";
import type {
  PosAnalyticsSummaryDto,
  PosRevenueTrendDto,
  PosTopProductsDto,
  PosCashierStatsDto,
} from "../types";

export interface PosDateRangeParams {
  from: string;
  to: string;
  store_id?: string;
}

function buildQs(entries: Array<[string, string | undefined]>): string {
  const qs = new URLSearchParams();
  entries.forEach(([k, v]) => {
    if (v !== undefined && v !== "") qs.set(k, v);
  });
  const s = qs.toString();
  return s ? `?${s}` : "";
}

function rangeEntries(p: PosDateRangeParams): Array<[string, string | undefined]> {
  return [
    ["from", p.from],
    ["to", p.to],
    ["store_id", p.store_id],
  ];
}

export const posAnalyticsApi = {
  getSummary: (params: PosDateRangeParams) =>
    api.get<PosAnalyticsSummaryDto>(
      `/api/analytics/pos/summary${buildQs(rangeEntries(params))}`,
    ),

  getRevenueTrend: (params: PosDateRangeParams & { group_by?: "day" | "week" }) =>
    api.get<PosRevenueTrendDto>(
      `/api/analytics/pos/revenue-trend${buildQs([...rangeEntries(params), ["group_by", params.group_by]])}`,
    ),

  getTopProducts: (params: PosDateRangeParams & { limit?: string }) =>
    api.get<PosTopProductsDto>(
      `/api/analytics/pos/top-products${buildQs([...rangeEntries(params), ["limit", params.limit]])}`,
    ),

  getCashiers: (params: PosDateRangeParams) =>
    api.get<PosCashierStatsDto>(
      `/api/analytics/pos/cashiers${buildQs(rangeEntries(params))}`,
    ),
};
