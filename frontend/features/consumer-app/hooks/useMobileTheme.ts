"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { fetchMobileTheme, updateMobileTheme } from "../api/mobileTheme";
import type { UpdateMobileThemeRequest } from "../types";

export const MOBILE_THEME_KEY = ["mobile-theme"] as const;

/** GET /api/v1/mobile/theme — loads the tenant's current (or proposed-default) theme. */
export function useMobileTheme(enabled = true) {
  return useQuery({
    queryKey: MOBILE_THEME_KEY,
    queryFn: fetchMobileTheme,
    enabled,
    staleTime: 30_000,
    retry: false,
  });
}

/**
 * PUT /api/v1/mobile/theme — full replace of every whitelisted field.
 *
 * LIVE-EFFECT CAVEAT (see MobileThemeService's remarks, mirrored in types.ts above): a
 * successful save here is read by `GET /api/v1/mobile/config` immediately — there is no
 * draft/publish protection for theme yet. `onSuccess` seeds the cache directly with the
 * server's response (rather than just invalidating) so the just-saved values are what the form
 * re-syncs to, without an extra round trip.
 */
export function useUpdateMobileTheme() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateMobileThemeRequest) => updateMobileTheme(body),
    onSuccess: (dto) => {
      qc.setQueryData(MOBILE_THEME_KEY, dto);
    },
  });
}
