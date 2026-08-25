import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { locationsApi, type CreateZoneDto } from "../api/locations";
import { stockApi } from "@/features/shelf/api/stock";
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

// Per-zone safe/warning/critical/expired counts for one location, derived from /api/stock.
// Scoped server-side to exactly this location (store_id param) — no tenant-wide overfetch, and
// no dependency on the global header store selector (this page's scope is the URL's locationId).
// pageSize is bumped to the backend's max clamp (200, see api-contracts.md) rather than left at
// the 50 default — a single location's batch count can exceed 50, which would otherwise silently
// truncate the counts computed below.
export function useZoneStatusCounts(locationId: string | null) {
  return useQuery({
    queryKey: ["locations", locationId, "zone-status"],
    queryFn: async () => {
      const page = await stockApi.getAll({ store_id: locationId!, pageSize: 200 });
      const byZone = new Map<string, ZoneStatusCounts>();
      for (const b of page.items) {
        if (b.storeId !== locationId || !b.zoneId || b.quantity <= 0) continue;
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
