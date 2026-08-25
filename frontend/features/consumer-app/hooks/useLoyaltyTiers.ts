"use client";

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { fetchLoyaltyTiers, updateLoyaltyTiers } from "../api/loyaltyTiers";
import type { UpsertTierRequest } from "../types";

export const LOYALTY_TIERS_KEY = ["loyalty-tiers"] as const;

/** GET /api/settings/loyalty/tiers — loads the tenant's current tier ladder (empty when none saved yet). */
export function useLoyaltyTiers(enabled = true) {
  return useQuery({
    queryKey: LOYALTY_TIERS_KEY,
    queryFn: fetchLoyaltyTiers,
    enabled,
    staleTime: 30_000,
    retry: false,
  });
}

/** PUT /api/settings/loyalty/tiers — full bulk replace of the ladder, keyed by sortOrder. */
export function useUpdateLoyaltyTiers() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpsertTierRequest[]) => updateLoyaltyTiers(body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: LOYALTY_TIERS_KEY });
    },
  });
}
