import { useCallback } from "react";
import { useQuery } from "@tanstack/react-query";
import { geoApi } from "../api/geo-api";
import { regionLabel } from "../lib/regionLabel";

export const GEO_KEYS = {
  regions: ["geo", "regions"] as const,
};

/**
 * GET /api/geo/regions — static registry, cached for the whole session
 * (`staleTime: Infinity`, same as `useItemCategories`).
 */
export function useRegions() {
  return useQuery({
    queryKey: GEO_KEYS.regions,
    queryFn: () => geoApi.getRegions(),
    staleTime: Infinity,
  });
}

/**
 * Returns a stable `(code) => label` resolver backed by the region registry.
 * Falls back to the raw code while the registry loads or when a code is unknown.
 */
export function useRegionLabel(): (code: string) => string {
  const { data: regions } = useRegions();
  return useCallback(
    (code: string) => regionLabel(code, regions ?? []),
    [regions],
  );
}
