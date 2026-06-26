import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { locationsApi, type CreateZoneDto } from "../api/locations";
import type { FloorPlanLayout, LocationZoneDto, ShelfPlanLayout, ZoneStatusCounts } from "../types";

export function parseFloorPlan(raw: string | null): FloorPlanLayout {
  const empty: FloorPlanLayout = { version: 1, grid: 20, canvasW: 1400, canvasH: 900, zones: [] };
  if (!raw) return empty;
  try {
    const parsed = JSON.parse(raw) as Partial<FloorPlanLayout>;
    if (!Array.isArray(parsed.zones)) return empty;
    return {
      version: 1,
      grid: parsed.grid ?? 20,
      canvasW: parsed.canvasW ?? 1400,
      canvasH: parsed.canvasH ?? 900,
      zones: parsed.zones,
    };
  } catch {
    return empty;
  }
}

export function parseShelfPlan(raw: string | null): ShelfPlanLayout {
  const empty: ShelfPlanLayout = { version: 1, grid: 20, canvasW: 1000, canvasH: 600, items: [] };
  if (!raw) return empty;
  try {
    const parsed = JSON.parse(raw) as Partial<ShelfPlanLayout>;
    if (!Array.isArray(parsed.items)) return empty;
    return {
      version: 1,
      grid: parsed.grid ?? 20,
      canvasW: parsed.canvasW ?? 1000,
      canvasH: parsed.canvasH ?? 600,
      items: parsed.items,
    };
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

export function useCreateZone(locationId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateZoneDto) => locationsApi.createZone(locationId, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["locations"] }),
  });
}

export function useUpdateZonePosition(locationId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ zone, shelfLayout }: { zone: LocationZoneDto; shelfLayout: ShelfPlanLayout }) =>
      locationsApi.updateZone(locationId, zone.id, {
        name: zone.name,
        type: zone.type,
        shelvesCount: zone.shelvesCount,
        tempMin: zone.tempMin,
        tempMax: zone.tempMax,
        isActive: zone.isActive,
        position: JSON.stringify(shelfLayout),
      }),
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
