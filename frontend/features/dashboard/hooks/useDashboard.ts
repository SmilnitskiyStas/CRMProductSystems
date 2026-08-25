"use client";

import { useQuery } from "@tanstack/react-query";
import { useStoreContext, useStoreScopeReady } from "@/lib/useStoreContext";
import { dashboardApi } from "../api/dashboard";

export function useDashboardStats() {
  const selectedStoreIds = useStoreContext((s) => s.selectedStoreIds);
  const ready = useStoreScopeReady();
  const query = useQuery({
    queryKey: ["dashboard", "stats", selectedStoreIds] as const,
    queryFn: () => dashboardApi.getStats(selectedStoreIds),
    staleTime: 60_000,
    enabled: ready,
  });
  return { ...query, isLoading: !ready || query.isLoading };
}

export function useAttentionItems() {
  const selectedStoreIds = useStoreContext((s) => s.selectedStoreIds);
  const ready = useStoreScopeReady();
  const query = useQuery({
    queryKey: ["dashboard", "attention", selectedStoreIds] as const,
    queryFn: () => dashboardApi.getAttentionItems(selectedStoreIds),
    staleTime: 60_000,
    enabled: ready,
  });
  return { ...query, isLoading: !ready || query.isLoading };
}

export function useStoreZones() {
  const selectedStoreIds = useStoreContext((s) => s.selectedStoreIds);
  const ready = useStoreScopeReady();
  const query = useQuery({
    queryKey: ["dashboard", "zones", selectedStoreIds] as const,
    queryFn: () => dashboardApi.getStoreZones(selectedStoreIds),
    staleTime: 5 * 60_000,
    enabled: ready,
  });
  return { ...query, isLoading: !ready || query.isLoading };
}

/** Period-over-period comparison for the expiry status cards (ADR-016). */
export function useExpirySummaryCompare(compareWeeksAgo = 1) {
  const selectedStoreIds = useStoreContext((s) => s.selectedStoreIds);
  const ready = useStoreScopeReady();
  const query = useQuery({
    queryKey: ["dashboard", "expiry-compare", selectedStoreIds, compareWeeksAgo] as const,
    queryFn: () => dashboardApi.getExpirySummaryCompare(selectedStoreIds, compareWeeksAgo),
    staleTime: 5 * 60_000,
    enabled: ready,
  });
  return { ...query, isLoading: !ready || query.isLoading };
}

/** Weekly KPI cards (sales/revenue/write-off loss, last 7d vs prior 7d). */
export function useWeeklyKpi() {
  const selectedStoreIds = useStoreContext((s) => s.selectedStoreIds);
  const ready = useStoreScopeReady();
  const query = useQuery({
    queryKey: ["dashboard", "weekly-kpi", selectedStoreIds] as const,
    queryFn: () => dashboardApi.getWeeklyKpi(selectedStoreIds),
    staleTime: 60_000,
    enabled: ready,
  });
  return { ...query, isLoading: !ready || query.isLoading };
}
