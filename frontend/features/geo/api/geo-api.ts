import { api } from "@/lib/api";
import type { Region } from "../types";

export const geoApi = {
  /**
   * GET /api/geo/regions — static Ukraine region registry (oblasts + major cities),
   * single source of truth on the backend (`UkraineRegions` constant). Anonymous-
   * cacheable, same pattern as `/api/marketplace/item-categories`. Never hard-code
   * the region list on the client — always read it from here via `useRegions()`.
   */
  getRegions: (): Promise<Region[]> => api.get<Region[]>("/api/geo/regions"),
};

/** Convenience alias for `geoApi.getRegions()`. */
export const getRegions = (): Promise<Region[]> => geoApi.getRegions();
