"use client";

import { useQuery } from "@tanstack/react-query";
import { usePrimaryStoreId } from "@/lib/useStoreContext";
import { dashboardApi } from "../api/dashboard";

export function useDashboardStats() {
  const primaryStoreId = usePrimaryStoreId();
  return useQuery({
    queryKey: ["dashboard", "stats", primaryStoreId] as const,
    queryFn: () => dashboardApi.getStats(primaryStoreId ?? null),
    staleTime: 60_000,
  });
}

export function useAttentionItems() {
  const primaryStoreId = usePrimaryStoreId();
  return useQuery({
    queryKey: ["dashboard", "attention", primaryStoreId] as const,
    queryFn: () => dashboardApi.getAttentionItems(primaryStoreId ?? null),
    staleTime: 60_000,
  });
}

export function useStoreZones() {
  const primaryStoreId = usePrimaryStoreId();
  return useQuery({
    queryKey: ["dashboard", "zones", primaryStoreId] as const,
    queryFn: () => dashboardApi.getStoreZones(primaryStoreId ?? null),
    staleTime: 5 * 60_000,
  });
}

/** Period-over-period comparison for the expiry status cards (ADR-016). */
export function useExpirySummaryCompare(compareWeeksAgo = 1) {
  const primaryStoreId = usePrimaryStoreId();
  return useQuery({
    queryKey: ["dashboard", "expiry-compare", primaryStoreId, compareWeeksAgo] as const,
    queryFn: () => dashboardApi.getExpirySummaryCompare(primaryStoreId ?? null, compareWeeksAgo),
    staleTime: 5 * 60_000,
  });
}

/** Weekly KPI cards (sales/revenue/write-off loss, last 7d vs prior 7d). */
export function useWeeklyKpi() {
  const primaryStoreId = usePrimaryStoreId();
  return useQuery({
    queryKey: ["dashboard", "weekly-kpi", primaryStoreId] as const,
    queryFn: () => dashboardApi.getWeeklyKpi(primaryStoreId ?? null),
    staleTime: 60_000,
  });
}
