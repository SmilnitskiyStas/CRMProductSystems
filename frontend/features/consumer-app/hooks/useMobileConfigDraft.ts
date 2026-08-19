"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { fetchMobileConfigDraft, saveMobileConfigDraft } from "../api/mobileConfigDraft";
import type { MobileConfigDraftResponse, SaveMobileConfigDraftRequest } from "../types";

export const MOBILE_CONFIG_DRAFT_KEY = ["mobile-config-draft"] as const;

/** GET /api/v1/mobile/config/draft — the tenant's current draft, or `hasDraft: false`. */
export function useMobileConfigDraft(enabled = true) {
  return useQuery({
    queryKey: MOBILE_CONFIG_DRAFT_KEY,
    queryFn: fetchMobileConfigDraft,
    enabled,
    staleTime: 30_000,
    retry: false,
  });
}

/**
 * PUT /api/v1/mobile/config/draft — whole-document replace. Callers must read-modify-write (see
 * `AppBuilderCanvas.tsx`) — this mutation has no partial-update semantics of its own, it sends
 * exactly the `configurationJson` string it's given. `onSuccess` seeds the cache with the server's
 * response directly (same convention as `useUpdateMobileTheme`) so the canvas re-syncs to the
 * just-saved document without an extra round trip.
 */
export function useSaveMobileConfigDraft() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: SaveMobileConfigDraftRequest) => saveMobileConfigDraft(body),
    onSuccess: (dto: MobileConfigDraftResponse) => {
      qc.setQueryData(MOBILE_CONFIG_DRAFT_KEY, dto);
    },
  });
}
