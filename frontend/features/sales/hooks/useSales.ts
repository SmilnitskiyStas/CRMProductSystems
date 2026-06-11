import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { salesApi } from "../api/sales";
import type { SalesFilters, UpsertDailySalePayload } from "../types";

const SALES_KEY = ["daily-sales"] as const;

export function useDailySales(filters: SalesFilters) {
  return useQuery({
    queryKey: [...SALES_KEY, filters],
    queryFn: () => salesApi.getAll(filters),
  });
}

export function useUpsertSale() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: UpsertDailySalePayload) => salesApi.upsert(payload),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SALES_KEY }),
  });
}

export function useImportCsv() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ storeId, file }: { storeId: string; file: File }) =>
      salesApi.importCsv(storeId, file),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SALES_KEY }),
  });
}

export function useMarkAnomaly() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, isAnomaly }: { id: string; isAnomaly: boolean }) =>
      salesApi.markAnomaly(id, isAnomaly),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SALES_KEY }),
  });
}
