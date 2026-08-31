import { useCallback } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getRegions } from './api';

export const GEO_KEYS = {
  regions: ['geo', 'regions'] as const,
};

/**
 * GET /api/geo/regions — static registry, cached for the whole session
 * (`staleTime: Infinity`, same as the web `useRegions` / `useItemCategories`).
 */
export function useRegions() {
  return useQuery({
    queryKey: GEO_KEYS.regions,
    queryFn: getRegions,
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
    (code: string) => regions?.find((r) => r.code === code)?.nameUa ?? code,
    [regions],
  );
}
