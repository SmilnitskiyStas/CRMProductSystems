"use client";

import { useQuery } from "@tanstack/react-query";
import { dashboardApi } from "../api/dashboard";

export function useDashboardStats() {
  return useQuery({
    queryKey: ["dashboard", "stats"] as const,
    queryFn: dashboardApi.getStats,
    staleTime: 60_000,
  });
}

export function useAttentionItems() {
  return useQuery({
    queryKey: ["dashboard", "attention"] as const,
    queryFn: dashboardApi.getAttentionItems,
    staleTime: 60_000,
  });
}

export function useStoreZones() {
  return useQuery({
    queryKey: ["dashboard", "zones"] as const,
    queryFn: dashboardApi.getStoreZones,
    staleTime: 5 * 60_000,
  });
}
