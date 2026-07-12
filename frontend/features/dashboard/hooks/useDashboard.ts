"use client";

import { useQuery } from "@tanstack/react-query";
import { useStoreContext } from "@/lib/useStoreContext";
import { dashboardApi } from "../api/dashboard";

export function useDashboardStats() {
  const { selectedStoreId } = useStoreContext();
  return useQuery({
    queryKey: ["dashboard", "stats", selectedStoreId] as const,
    queryFn: () => dashboardApi.getStats(selectedStoreId),
    staleTime: 60_000,
  });
}

export function useAttentionItems() {
  const { selectedStoreId } = useStoreContext();
  return useQuery({
    queryKey: ["dashboard", "attention", selectedStoreId] as const,
    queryFn: () => dashboardApi.getAttentionItems(selectedStoreId),
    staleTime: 60_000,
  });
}

export function useStoreZones() {
  const { selectedStoreId } = useStoreContext();
  return useQuery({
    queryKey: ["dashboard", "zones", selectedStoreId] as const,
    queryFn: () => dashboardApi.getStoreZones(selectedStoreId),
    staleTime: 5 * 60_000,
  });
}

/** Period-over-period comparison for the expiry status cards (ADR-016). */
export function useExpirySummaryCompare(compareWeeksAgo = 1) {
  const { selectedStoreId } = useStoreContext();
  return useQuery({
    queryKey: ["dashboard", "expiry-compare", selectedStoreId, compareWeeksAgo] as const,
    queryFn: () => dashboardApi.getExpirySummaryCompare(selectedStoreId, compareWeeksAgo),
    staleTime: 5 * 60_000,
  });
}

/** Weekly KPI cards (sales/revenue/write-off loss, last 7d vs prior 7d). */
export function useWeeklyKpi() {
  const { selectedStoreId } = useStoreContext();
  return useQuery({
    queryKey: ["dashboard", "weekly-kpi", selectedStoreId] as const,
    queryFn: () => dashboardApi.getWeeklyKpi(selectedStoreId),
    staleTime: 60_000,
  });
}
