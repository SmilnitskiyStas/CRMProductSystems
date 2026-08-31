import { apiClient } from '@/lib/api-client';
import type { Region } from './types';

/**
 * GET /api/geo/regions — static Ukraine region registry (oblasts + major cities),
 * single source of truth on the backend (`UkraineRegions` constant). Anonymous-
 * cacheable, same pattern as `/marketplace/item-categories`. Never hard-code the
 * region list on the client — always read it from here via `useRegions()`.
 */
export async function getRegions(): Promise<Region[]> {
  const { data } = await apiClient.get<Region[]>('/geo/regions');
  return Array.isArray(data) ? data : [];
}
