import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { orderTrackingApi } from "../api/orderTracking";
import type { OrderTrackingFilters } from "../types";

const KEY = ["order-tracking"] as const;

export function useOrderTracking(filters: OrderTrackingFilters) {
  return useQuery({
    queryKey: [...KEY, "page", filters],
    queryFn: () => orderTrackingApi.getPage(filters),
    placeholderData: keepPreviousData,
  });
}

export function useOrderTrackingSummary(storeId?: string) {
  return useQuery({
    queryKey: [...KEY, "summary", storeId],
    queryFn: () => orderTrackingApi.getSummary(storeId),
  });
}

export function usePostponeOrder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (v: { id: string; newExpectedAt: string; reason?: string }) =>
      orderTrackingApi.postpone(v.id, v.newExpectedAt, v.reason),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: KEY });
      // The order calculation and the receipts list read the same documents.
      queryClient.invalidateQueries({ queryKey: ["receipts"] });
    },
  });
}
