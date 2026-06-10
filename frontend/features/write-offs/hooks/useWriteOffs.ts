import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { writeOffsApi } from "../api/writeOffs";
import type { CreateWriteOffRequest } from "../types";

export function useWriteOffs(params?: { store_id?: string; status?: string }) {
  return useQuery({
    queryKey: ["write-offs", params],
    queryFn: () => writeOffsApi.getAll(params),
  });
}

export function useWriteOffDetail(id: string | null) {
  return useQuery({
    queryKey: ["write-offs", id],
    queryFn: () => writeOffsApi.getById(id!),
    enabled: !!id,
  });
}

export function useCreateWriteOff() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateWriteOffRequest) => writeOffsApi.create(data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["write-offs"] }),
  });
}

export function useApproveWriteOff() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => writeOffsApi.approve(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["write-offs"] });
      qc.invalidateQueries({ queryKey: ["stock"] });
    },
  });
}

export function useRejectWriteOff() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => writeOffsApi.reject(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["write-offs"] }),
  });
}
