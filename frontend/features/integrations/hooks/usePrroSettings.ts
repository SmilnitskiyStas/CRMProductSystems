"use client";

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  fetchPrroSettings,
  updatePrroSettings,
  testPrroConnection,
} from "../api/prro";
import type { UpdatePrroSettingsRequest } from "../api/prro";

export const PRRO_SETTINGS_KEY = ["prro-settings"] as const;

/** GET /api/settings/prro — loads current masked settings */
export function usePrroSettings(enabled = true) {
  return useQuery({
    queryKey: PRRO_SETTINGS_KEY,
    queryFn: fetchPrroSettings,
    enabled,
    staleTime: 30_000,
    retry: false,
  });
}

/** PUT /api/settings/prro — saves settings; masked values are kept server-side */
export function useUpdatePrroSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdatePrroSettingsRequest) => updatePrroSettings(body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: PRRO_SETTINGS_KEY });
    },
  });
}

/**
 * POST /api/settings/prro/test — fires a live connection test.
 * This is NOT a query (no caching); result is shown inline in the modal.
 */
export function useTestPrroConnection() {
  return useMutation({
    mutationFn: testPrroConnection,
  });
}
