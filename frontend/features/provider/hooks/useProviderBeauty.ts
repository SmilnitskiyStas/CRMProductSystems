"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { providerBeautyApi } from "../api/providerBeauty";
import type { CreateBeautyTenantRequest } from "../types";

export const beautyKey = (id: string) => ["provider", "beauty", id] as const;

export function useBeautyTenant(id: string) {
  return useQuery({
    queryKey: beautyKey(id),
    queryFn: () => providerBeautyApi.get(id),
    enabled: Boolean(id),
    staleTime: 15_000,
    retry: false,
  });
}

// Mutations that return a one-time inviteUrl use gcTime 0, so the secret is not kept in the
// mutation cache; callers additionally call reset() right after reading the result.
export function useCreateBeautyTenant(id: string) {
  const qc = useQueryClient();
  return useMutation({
    gcTime: 0,
    mutationFn: (req: CreateBeautyTenantRequest) => providerBeautyApi.create(req),
    onSuccess: () => qc.invalidateQueries({ queryKey: beautyKey(id) }),
  });
}

export function useBeautyOwnerInvite(id: string) {
  const qc = useQueryClient();
  return useMutation({
    gcTime: 0,
    mutationFn: () => providerBeautyApi.ownerInvite(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: beautyKey(id) }),
  });
}

export function useBeautySetSuspended(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (suspend: boolean) => (suspend ? providerBeautyApi.suspend(id) : providerBeautyApi.resume(id)),
    onSuccess: () => qc.invalidateQueries({ queryKey: beautyKey(id) }),
  });
}

export function useBeautyUpdateModules(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (modules: string[]) => providerBeautyApi.updateModules(id, modules),
    onSuccess: () => qc.invalidateQueries({ queryKey: beautyKey(id) }),
  });
}
