"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { legalEntitiesApi } from "../api/legal-entities";
import type { CreateLegalEntityDto, LegalEntityDto, UpdateLegalEntityDto } from "../types";

export const LEGAL_ENTITIES_KEY = ["legal-entities"] as const;

/** Fetch all legal entities in the tenant */
export function useLegalEntities() {
  return useQuery({
    queryKey: LEGAL_ENTITIES_KEY,
    queryFn: () => legalEntitiesApi.getAll(),
    staleTime: 2 * 60_000,
  });
}

/** Fetch a single legal entity */
export function useLegalEntity(id: string | null) {
  return useQuery({
    queryKey: [...LEGAL_ENTITIES_KEY, id],
    queryFn: () => legalEntitiesApi.getById(id!),
    enabled: !!id,
  });
}

/** Create a new legal entity */
export function useCreateLegalEntity() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateLegalEntityDto) => legalEntitiesApi.create(data),
    onSuccess: (created) => {
      qc.setQueryData<LegalEntityDto[]>(LEGAL_ENTITIES_KEY, (prev) =>
        prev ? [created, ...prev] : [created],
      );
    },
  });
}

/** Update a legal entity */
export function useUpdateLegalEntity(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: UpdateLegalEntityDto) => legalEntitiesApi.update(id, data),
    onSuccess: (updated) => {
      qc.setQueryData<LegalEntityDto[]>(LEGAL_ENTITIES_KEY, (prev) =>
        prev?.map((e) => (e.id === updated.id ? updated : e)),
      );
    },
  });
}

/** Deactivate (soft delete) a legal entity */
export function useDeactivateLegalEntity() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => legalEntitiesApi.deactivate(id),
    onSuccess: (_, id) => {
      qc.setQueryData<LegalEntityDto[]>(LEGAL_ENTITIES_KEY, (prev) =>
        prev?.map((e) => (e.id === id ? { ...e, isActive: false } : e)),
      );
    },
  });
}
