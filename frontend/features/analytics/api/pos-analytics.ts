import { api } from "@/lib/api";
import type {
  PosAnalyticsSummaryDto,
  PosAnalyticsSummaryCompareDto,
  PosRevenueTrendDto,
  PosRevenueTrendCompareDto,
  PosTopProductsDto,
  PosCashierStatsDto,
  ProductSalesTrendDto,
  ProductSalesTrendCompareDto,
  WorstProductsDto,
} from "../types";

export interface PosDateRangeParams {
  from: string;
  to: string;
  store_id?: string;
}

export interface PosCompareParams {
  compare?: boolean;
  compareFrom?: string;
  compareTo?: string;
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

function compareEntries(p: PosCompareParams | undefined): Array<[string, string | undefined]> {
  return [
    ["compare", p?.compare ? "true" : undefined],
    ["compareFrom", p?.compareFrom],
    ["compareTo", p?.compareTo],
  ];
}

// Backward compatible: `compare` omitted/false → flat DTO (unchanged shape).
// `compare: true` → wrapped comparison DTO.
function getSummary(params: PosDateRangeParams & { compare?: false }): Promise<PosAnalyticsSummaryDto>;
function getSummary(
  params: PosDateRangeParams & { compare: true } & PosCompareParams,
): Promise<PosAnalyticsSummaryCompareDto>;
function getSummary(
  params: PosDateRangeParams & PosCompareParams,
): Promise<PosAnalyticsSummaryDto | PosAnalyticsSummaryCompareDto> {
  return api.get(`/api/analytics/pos/summary${buildQs([...rangeEntries(params), ...compareEntries(params)])}`);
}

function getRevenueTrend(
  params: PosDateRangeParams & { group_by?: "day" | "week"; compare?: false },
): Promise<PosRevenueTrendDto>;
function getRevenueTrend(
  params: PosDateRangeParams & { group_by?: "day" | "week"; compare: true } & PosCompareParams,
): Promise<PosRevenueTrendCompareDto>;
function getRevenueTrend(
  params: PosDateRangeParams & { group_by?: "day" | "week" } & PosCompareParams,
): Promise<PosRevenueTrendDto | PosRevenueTrendCompareDto> {
  return api.get(
    `/api/analytics/pos/revenue-trend${buildQs([
      ...rangeEntries(params),
      ["group_by", params.group_by],
      ...compareEntries(params),
    ])}`,
  );
}

// TASK-484: row-click drill-down from top-products; productId is a path segment, not a query
// param. TASK-590 added the compare-mode overload (same split as getRevenueTrend above).
function getProductSalesTrend(
  productId: string,
  params: PosDateRangeParams & { group_by?: "day" | "week"; compare?: false },
): Promise<ProductSalesTrendDto>;
function getProductSalesTrend(
  productId: string,
  params: PosDateRangeParams & { group_by?: "day" | "week"; compare: true } & PosCompareParams,
): Promise<ProductSalesTrendCompareDto>;
function getProductSalesTrend(
  productId: string,
  params: PosDateRangeParams & { group_by?: "day" | "week" } & PosCompareParams,
): Promise<ProductSalesTrendDto | ProductSalesTrendCompareDto> {
  return api.get(
    `/api/analytics/pos/products/${productId}/trend${buildQs([
      ...rangeEntries(params),
      ["group_by", params.group_by],
      ...compareEntries(params),
    ])}`,
  );
}

export const posAnalyticsApi = {
  getSummary,

  getRevenueTrend,

  getTopProducts: (params: PosDateRangeParams & { limit?: string }) =>
    api.get<PosTopProductsDto>(
      `/api/analytics/pos/top-products${buildQs([...rangeEntries(params), ["limit", params.limit]])}`,
    ),

  // TASK-490/493: dead-stock counterpart to getTopProducts above. Same params shape (store_id/
  // from/to/limit, server-side clamped 1-100), ascending by salesRevenue instead of descending.
  getWorstProducts: (params: PosDateRangeParams & { limit?: string }) =>
    api.get<WorstProductsDto>(
      `/api/analytics/pos/worst-products${buildQs([...rangeEntries(params), ["limit", params.limit]])}`,
    ),

  getCashiers: (params: PosDateRangeParams) =>
    api.get<PosCashierStatsDto>(
      `/api/analytics/pos/cashiers${buildQs(rangeEntries(params))}`,
    ),

  getProductSalesTrend,
};
