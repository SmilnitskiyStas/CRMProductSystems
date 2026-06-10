import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { transfersApi } from "../api/transfers";
import type { CreateTransferRequest } from "../types";

export function useTransfers(params?: { store_id?: string; status?: string }, enabled = true) {
  return useQuery({
    queryKey: ["transfers", params],
    queryFn: () => transfersApi.getAll(params),
    enabled,
  });
}

export function useTransferDetail(id: string | null) {
  return useQuery({
    queryKey: ["transfers", id],
    queryFn: () => transfersApi.getById(id!),
    enabled: !!id,
  });
}

export function useCreateTransfer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateTransferRequest) => transfersApi.create(data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["transfers"] }),
  });
}

export function useConfirmTransfer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => transfersApi.confirm(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["transfers"] });
      qc.invalidateQueries({ queryKey: ["stock"] });
    },
  });
}

export function useCancelTransfer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => transfersApi.cancel(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["transfers"] }),
  });
}
