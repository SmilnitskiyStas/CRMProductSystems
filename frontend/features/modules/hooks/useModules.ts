"use client";

import { useQuery } from "@tanstack/react-query";
import { modulesApi } from "../api/modules";

export const MODULES_KEY = ["modules-settings"] as const;

/** GET /api/settings/modules — current tenant's business type + enabled modules. */
export function useModules(enabled = true) {
  return useQuery({
    queryKey: MODULES_KEY,
    queryFn: modulesApi.get,
    enabled,
    staleTime: 60_000,
    retry: false,
  });
}
