"use client";

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { fetchLoyaltySettings, updateLoyaltySettings } from "../api/loyaltySettings";
import type { UpdateLoyaltyProgramSettingsRequest } from "../types";

export const LOYALTY_SETTINGS_KEY = ["loyalty-settings"] as const;

/** GET /api/settings/loyalty — loads the tenant's current (or proposed-default) settings. */
export function useLoyaltySettings(enabled = true) {
  return useQuery({
    queryKey: LOYALTY_SETTINGS_KEY,
    queryFn: fetchLoyaltySettings,
    enabled,
    staleTime: 30_000,
    retry: false,
  });
}

/** PUT /api/settings/loyalty — full replace of all settings fields. */
export function useUpdateLoyaltySettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateLoyaltyProgramSettingsRequest) => updateLoyaltySettings(body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: LOYALTY_SETTINGS_KEY });
    },
  });
}
