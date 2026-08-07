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

// ── Category × product breakdown / losses × product (TASK-483) ─────────────
// NOTE: neither hook uses `placeholderData`/`keepPreviousData` on purpose (same discipline as
// useMarketingAnalyticsOverview etc. in useMarketingAnalytics.ts) — the full filter object is
// the query key, so switching category/store/reason/date-range is always a brand-new key and
// React Query shows a clean loading state instead of quietly keeping the previous selection's
// rows on screen.

export function useCategoryProductBreakdown(
  params: { category_id: string | null; store_id?: string; from?: string; to?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: ["analytics-category-products", params],
    queryFn: () => analyticsApi.getCategoryProductBreakdown(params),
    enabled,
  });
}

export function useLossesByProduct(
  params: { store_id?: string; reason?: string; from?: string; to?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: ["analytics-losses-by-product", params],
    queryFn: () => analyticsApi.getLossesByProduct(params),
    enabled,
  });
}
