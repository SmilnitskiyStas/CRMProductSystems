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
