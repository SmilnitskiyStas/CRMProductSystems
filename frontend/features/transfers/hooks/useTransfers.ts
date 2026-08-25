import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { transfersApi } from "../api/transfers";
import { useStoreScopeReady } from "@/lib/useStoreContext";
import type { CreateTransferRequest } from "../types";

export function useTransfers(
  params?: { store_id?: string; status?: string; page?: number; pageSize?: number },
  enabled = true,
) {
  const ready = useStoreScopeReady();
  const page = params?.page ?? 1;
  const pageSize = params?.pageSize ?? 50;
  const query = useQuery({
    queryKey: ["transfers", { ...params, page, pageSize }],
    queryFn: () => transfersApi.getAll({ ...params, page, pageSize }),
    enabled: enabled && ready,
    placeholderData: (prev) => prev,
  });
  return { ...query, isLoading: !ready || query.isLoading };
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
