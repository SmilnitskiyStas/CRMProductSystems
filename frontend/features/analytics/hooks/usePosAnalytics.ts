import { useQuery } from "@tanstack/react-query";
import { posAnalyticsApi, type PosDateRangeParams } from "../api/pos-analytics";

export function usePosSummary(params: PosDateRangeParams, enabled = true) {
  return useQuery({
    queryKey: ["pos-analytics-summary", params],
    queryFn: () => posAnalyticsApi.getSummary(params),
    enabled,
  });
}

export function usePosRevenueTrend(
  params: PosDateRangeParams & { group_by?: "day" | "week" },
  enabled = true,
) {
  return useQuery({
    queryKey: ["pos-analytics-trend", params],
    queryFn: () => posAnalyticsApi.getRevenueTrend(params),
    enabled,
  });
}

export function usePosTopProducts(
  params: PosDateRangeParams & { limit?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: ["pos-analytics-top-products", params],
    queryFn: () => posAnalyticsApi.getTopProducts(params),
    enabled,
  });
}

// TASK-490/493: dead-stock counterpart to usePosTopProducts above. Full filter object in the
// query key, same discipline as every hook in this file — no keepPreviousData.
export function useWorstProducts(
  params: PosDateRangeParams & { limit?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: ["pos-analytics-worst-products", params],
    queryFn: () => posAnalyticsApi.getWorstProducts(params),
    enabled,
  });
}

export function usePosCashiers(params: PosDateRangeParams, enabled = true) {
  return useQuery({
    queryKey: ["pos-analytics-cashiers", params],
    queryFn: () => posAnalyticsApi.getCashiers(params),
    enabled,
  });
}

// TASK-484: row-click drill-down from top-products, consumed by the extended
// ProductAnalyticsTab. Full filter object (productId + params) in the query key, same
// discipline as every hook above — no keepPreviousData.
export function useProductSalesTrend(
  productId: string,
  params: PosDateRangeParams & { group_by?: "day" | "week" },
  enabled = true,
) {
  return useQuery({
    queryKey: ["pos-analytics-product-trend", productId, params],
    queryFn: () => posAnalyticsApi.getProductSalesTrend(productId, params),
    enabled: enabled && !!productId,
  });
}

// TASK-590: compare-mode counterpart to useProductSalesTrend above, consumed by
// LinkedProductSalesCard (Events product-linking sales-comparison section).
export function useProductSalesTrendCompare(
  productId: string,
  params: PosDateRangeParams & { group_by?: "day" | "week" },
  enabled = true,
) {
  return useQuery({
    queryKey: ["pos-analytics-product-trend-compare", productId, params],
    queryFn: () => posAnalyticsApi.getProductSalesTrend(productId, { ...params, compare: true }),
    enabled: enabled && !!productId,
  });
}

// ── Period comparison (ADR-016) ─────────────────────────────────────────────

export function usePosSummaryCompare(
  params: PosDateRangeParams & { compareFrom?: string; compareTo?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: ["pos-analytics-summary-compare", params],
    queryFn: () => posAnalyticsApi.getSummary({ ...params, compare: true }),
    enabled,
  });
}

export function usePosRevenueTrendCompare(
  params: PosDateRangeParams & { group_by?: "day" | "week"; compareFrom?: string; compareTo?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: ["pos-analytics-trend-compare", params],
    queryFn: () => posAnalyticsApi.getRevenueTrend({ ...params, compare: true }),
    enabled,
  });
}
