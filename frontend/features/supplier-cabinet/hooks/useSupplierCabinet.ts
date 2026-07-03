"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { supplierCabinetApi } from "../api/supplier-cabinet-api";
import type {
  CabinetProfileUpdateRequest,
  CabinetAddItemRequest,
  CabinetUpdateItemRequest,
} from "../types";

// ─── Query keys ───────────────────────────────────────────────────────────────

export const CABINET_KEYS = {
  profile: ["supplier-cabinet", "profile"] as const,
  items: ["supplier-cabinet", "items"] as const,
  reviews: (page: number) => ["supplier-cabinet", "reviews", page] as const,
  metrics: ["supplier-cabinet", "metrics"] as const,
};

// ─── Profile ──────────────────────────────────────────────────────────────────

export function useCabinetProfile() {
  return useQuery({
    queryKey: CABINET_KEYS.profile,
    queryFn: supplierCabinetApi.getProfile,
    staleTime: 30_000,
    retry: false,
  });
}

export function useUpdateCabinetProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CabinetProfileUpdateRequest) =>
      supplierCabinetApi.updateProfile(body),
    onSuccess: (profile) => {
      queryClient.setQueryData(CABINET_KEYS.profile, profile);
    },
  });
}

export function useTogglePublish() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: supplierCabinetApi.togglePublish,
    onSuccess: (profile) => {
      queryClient.setQueryData(CABINET_KEYS.profile, profile);
    },
  });
}

// ─── Items ────────────────────────────────────────────────────────────────────

export function useCabinetItems() {
  return useQuery({
    queryKey: CABINET_KEYS.items,
    queryFn: supplierCabinetApi.getItems,
    staleTime: 30_000,
    retry: false,
  });
}

export function useAddCabinetItem() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CabinetAddItemRequest) => supplierCabinetApi.addItem(body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_KEYS.items });
    },
  });
}

export function useUpdateCabinetItem() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: CabinetUpdateItemRequest }) =>
      supplierCabinetApi.updateItem(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_KEYS.items });
    },
  });
}

export function useDeleteCabinetItem() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => supplierCabinetApi.deleteItem(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_KEYS.items });
    },
  });
}

// ─── Reviews / metrics ────────────────────────────────────────────────────────

export function useCabinetReviews(page = 1, pageSize = 20) {
  return useQuery({
    queryKey: CABINET_KEYS.reviews(page),
    queryFn: () => supplierCabinetApi.getReviews(page, pageSize),
    staleTime: 30_000,
    retry: false,
  });
}

export function useCabinetMetrics() {
  return useQuery({
    queryKey: CABINET_KEYS.metrics,
    queryFn: supplierCabinetApi.getMetrics,
    staleTime: 30_000,
    retry: false,
  });
}
