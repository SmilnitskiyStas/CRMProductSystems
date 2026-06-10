import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { receiptsApi } from "../api/receipts";
import type { CreateReceiptRequest, UpdateItemPayload } from "../types";

export function useReceipts(params?: { store_id?: string; status?: string }, enabled = true) {
  return useQuery({
    queryKey: ["receipts", params],
    queryFn: () => receiptsApi.getAll(params),
    enabled,
  });
}

export function useReceiptDetail(id: string | null) {
  return useQuery({
    queryKey: ["receipts", id],
    queryFn: () => receiptsApi.getById(id!),
    enabled: !!id,
  });
}

export function useCreateReceipt() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateReceiptRequest) => receiptsApi.create(data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["receipts"] }),
  });
}

export function useUpdateReceiptItems(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (items: UpdateItemPayload[]) => receiptsApi.updateItems(id, items),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["receipts", id] }),
  });
}

export function useReceiveReceipt() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => receiptsApi.receive(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["receipts"] });
      qc.invalidateQueries({ queryKey: ["stock"] });
    },
  });
}

export function useCancelReceipt() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => receiptsApi.cancel(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["receipts"] }),
  });
}
