"use client";

import { useQuery } from "@tanstack/react-query";
import { fetchBlockRegistry } from "../api/blockRegistry";

export const BLOCK_REGISTRY_KEY = ["mobile-block-registry"] as const;

/**
 * GET /api/v1/mobile/blocks — the server-owned block catalog (TASK-538) backing the App Builder
 * palette. Not tenant-scoped, compile-time-fixed data, so a long `staleTime` is safe (matches the
 * "static reference data" convention `MOBILE_CONFIG_BLOCK_TYPES` etc. in `../types` document —
 * this hook fetches the richer server copy instead of hand-maintaining display metadata client-side).
 */
export function useBlockRegistry() {
  return useQuery({
    queryKey: BLOCK_REGISTRY_KEY,
    queryFn: fetchBlockRegistry,
    staleTime: 5 * 60_000,
    retry: false,
  });
}
