"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { bannersApi } from "../api/banners";
import type { CreateBannerRequest, UpdateBannerRequest } from "../types";

export const BANNERS_KEY = ["banners"] as const;

/** GET /api/banners — full list for the current tenant. */
export function useBanners() {
  return useQuery({
    queryKey: BANNERS_KEY,
    queryFn: () => bannersApi.getAll(),
  });
}

/** GET /api/banners/{id} — used to pre-fill the edit form. */
export function useBanner(id: string | null) {
  return useQuery({
    queryKey: [...BANNERS_KEY, id],
    queryFn: () => bannersApi.getById(id!),
    enabled: !!id,
  });
}

export function useCreateBanner() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateBannerRequest) => bannersApi.create(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: BANNERS_KEY }),
  });
}

export function useUpdateBanner() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateBannerRequest }) => bannersApi.update(id, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: BANNERS_KEY }),
  });
}

/** DELETE /api/banners/{id} — soft-hide, never a hard delete. */
export function useDeactivateBanner() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => bannersApi.deactivate(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: BANNERS_KEY }),
  });
}

export function useUploadBannerImage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, file }: { id: string; file: File }) => bannersApi.uploadImage(id, file),
    onSuccess: () => qc.invalidateQueries({ queryKey: BANNERS_KEY }),
  });
}

/** GET /api/banners/{id}/analytics — only fetched once the row's analytics panel is opened. */
export function useBannerAnalytics(id: string | null) {
  return useQuery({
    queryKey: [...BANNERS_KEY, id, "analytics"],
    queryFn: () => bannersApi.getAnalytics(id!),
    enabled: !!id,
  });
}
