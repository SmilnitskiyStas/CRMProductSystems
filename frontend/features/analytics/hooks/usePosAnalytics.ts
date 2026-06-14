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

export function usePosCashiers(params: PosDateRangeParams, enabled = true) {
  return useQuery({
    queryKey: ["pos-analytics-cashiers", params],
    queryFn: () => posAnalyticsApi.getCashiers(params),
    enabled,
  });
}
