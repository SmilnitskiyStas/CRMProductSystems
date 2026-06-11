import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { aiOrdersApi } from "../api/aiOrders";

const KEY = ["ai-orders"] as const;

export function useAiOrders(storeId?: string) {
  return useQuery({
    queryKey: [...KEY, storeId ?? "all"],
    queryFn: () => aiOrdersApi.getList(storeId),
  });
}

export function useAiOrder(id: string | null) {
  return useQuery({
    queryKey: [...KEY, "detail", id],
    queryFn: () => aiOrdersApi.getById(id!),
    enabled: !!id,
  });
}

function useInvalidate() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: KEY });
}

export function useGenerateAiOrder() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (storeId: string) => aiOrdersApi.generate(storeId),
    onSuccess: invalidate,
  });
}

export function useUpdateAiOrderItem() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: ({ orderId, itemId, quantityFinal, editReason }: {
      orderId: string; itemId: string; quantityFinal: number; editReason: string | null;
    }) => aiOrdersApi.updateItem(orderId, itemId, quantityFinal, editReason),
    onSuccess: invalidate,
  });
}

export function useAcceptAiOrder() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (id: string) => aiOrdersApi.accept(id),
    onSuccess: invalidate,
  });
}

export function useRejectAiOrder() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (id: string) => aiOrdersApi.reject(id),
    onSuccess: invalidate,
  });
}
