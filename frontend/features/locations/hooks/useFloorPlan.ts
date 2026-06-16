import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { locationsApi } from "../api/locations";
import type { FloorPlanLayout, ZoneStatusCounts } from "../types";

export function parseFloorPlan(raw: string | null): FloorPlanLayout {
  const empty: FloorPlanLayout = { version: 1, grid: 20, zones: [] };
  if (!raw) return empty;
  try {
    const parsed = JSON.parse(raw) as Partial<FloorPlanLayout>;
    if (!Array.isArray(parsed.zones)) return empty;
    return { version: 1, grid: parsed.grid ?? 20, zones: parsed.zones };
  } catch {
    return empty;
  }
}

export function useUpdateFloorPlan(locationId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (layout: FloorPlanLayout) =>
      locationsApi.updateFloorPlan(locationId, JSON.stringify(layout)),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["locations"] }),
  });
}

// Per-zone safe/warning/critical/expired counts for one location, derived from /api/stock
export function useZoneStatusCounts(locationId: string | null) {
  return useQuery({
    queryKey: ["locations", locationId, "zone-status"],
    queryFn: async () => {
      const batches = await locationsApi.getStock();
      const byZone = new Map<string, ZoneStatusCounts>();
      for (const b of batches) {
        if (b.locationId !== locationId || !b.zoneId || b.quantity <= 0) continue;
        let counts = byZone.get(b.zoneId);
        if (!counts) {
          counts = { safe: 0, warning: 0, critical: 0, expired: 0 };
          byZone.set(b.zoneId, counts);
        }
        if (b.status in counts) counts[b.status as keyof ZoneStatusCounts]++;
      }
      return byZone;
    },
    enabled: !!locationId,
  });
}
