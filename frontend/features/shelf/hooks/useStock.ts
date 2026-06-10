import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { stockApi } from "../api/stock";
import type { CreateStockRequest } from "../types";

export function useStock(params?: {
  store_id?: string;
  status?: string;
  zone_id?: string;
  product_id?: string;
}) {
  return useQuery({
    queryKey: ["stock", params],
    queryFn: () => stockApi.getAll(params),
  });
}

export function useStockById(id: string | null) {
  return useQuery({
    queryKey: ["stock", id],
    queryFn: () => stockApi.getById(id!),
    enabled: !!id,
  });
}

export function useSuggestions(store_id?: string) {
  return useQuery({
    queryKey: ["stock-suggestions", store_id],
    queryFn: () => stockApi.getSuggestions(store_id),
  });
}

export function useCreateStock() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateStockRequest) => stockApi.create(data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["stock"] });
    },
  });
}

export function useVerifyStock() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => stockApi.verify(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["stock"] });
    },
  });
}
