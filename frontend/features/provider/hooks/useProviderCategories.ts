"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { providerCategoriesApi } from "../api/providerCategories";
import type { PlatformCategoryDto, CreateCategoryBody, UpdateCategoryBody } from "../types";

export const PROVIDER_CATEGORIES_KEY = ["provider", "categories"] as const;
// The tenant-facing dropdown (features/inventory/hooks/useCategories.ts) reads this key —
// invalidate it after every mutation so a provider edit shows up in the tenant UI.
const TENANT_CATEGORIES_KEY = ["categories"] as const;

export function useProviderCategories() {
  return useQuery({
    queryKey: PROVIDER_CATEGORIES_KEY,
    queryFn: async (): Promise<PlatformCategoryDto[]> => {
      try {
        return await providerCategoriesApi.list();
      } catch {
        return [];
      }
    },
    staleTime: 60_000,
    retry: false,
  });
}

function useInvalidateCategories() {
  const qc = useQueryClient();
  return () => {
    qc.invalidateQueries({ queryKey: PROVIDER_CATEGORIES_KEY });
    qc.invalidateQueries({ queryKey: TENANT_CATEGORIES_KEY });
  };
}

export function useCreateCategory() {
  const invalidate = useInvalidateCategories();
  return useMutation({
    mutationFn: (body: CreateCategoryBody) => providerCategoriesApi.create(body),
    onSuccess: invalidate,
  });
}

export function useUpdateCategory() {
  const invalidate = useInvalidateCategories();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateCategoryBody }) =>
      providerCategoriesApi.update(id, body),
    onSuccess: invalidate,
  });
}

export function useDeleteCategory() {
  const invalidate = useInvalidateCategories();
  return useMutation({
    mutationFn: (id: string) => providerCategoriesApi.remove(id),
    onSuccess: invalidate,
  });
}
