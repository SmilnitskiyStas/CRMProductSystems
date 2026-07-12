import { useQuery } from "@tanstack/react-query";
import { analyticsApi } from "../api/analytics";

export function useExpirySummary(params?: { store_id?: string; network?: boolean }, enabled = true) {
  return useQuery({
    queryKey: ["analytics-expiry", params],
    queryFn: () => analyticsApi.getExpirySummary(params),
    enabled,
  });
}

export function useWriteOffAnalytics(params?: { store_id?: string; from?: string; to?: string }, enabled = true) {
  return useQuery({
    queryKey: ["analytics-writeoffs", params],
    queryFn: () => analyticsApi.getWriteOffs(params),
    enabled,
  });
}

export function useMovementAnalytics(
  params?: { store_id?: string; type?: string; from?: string; to?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: ["analytics-movements", params],
    queryFn: () => analyticsApi.getMovements(params),
    enabled,
  });
}

export function useZoneAnalytics(store_id?: string, enabled = true) {
  return useQuery({
    queryKey: ["analytics-zone", store_id],
    queryFn: () => analyticsApi.getByZone(store_id),
    enabled,
  });
}

export function useCategoryAnalytics(store_id?: string, enabled = true) {
  return useQuery({
    queryKey: ["analytics-category", store_id],
    queryFn: () => analyticsApi.getByCategory(store_id),
    enabled,
  });
}

export function useLosses(params?: { store_id?: string; from?: string; to?: string }, enabled = true) {
  return useQuery({
    queryKey: ["analytics-losses", params],
    queryFn: () => analyticsApi.getLosses(params),
    enabled,
  });
}

// ── Period comparison (ADR-016) ─────────────────────────────────────────────

export function useWriteOffAnalyticsCompare(
  params: { store_id?: string; from?: string; to?: string; compareFrom?: string; compareTo?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: ["analytics-writeoffs-compare", params],
    queryFn: () => analyticsApi.getWriteOffs({ ...params, compare: true }),
    enabled,
  });
}

export function useLossesCompare(
  params: { store_id?: string; from?: string; to?: string; compareFrom?: string; compareTo?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: ["analytics-losses-compare", params],
    queryFn: () => analyticsApi.getLosses({ ...params, compare: true }),
    enabled,
  });
}
