import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { locationsApi, type CreateZoneDto } from "../api/locations";
import { stockApi } from "@/features/shelf/api/stock";
import type {
  FloorPlanLayout,
  LocationZoneDto,
  ShelfPlanLayout,
  ZoneItemSummary,
  ZoneStatusCounts,
} from "../types";

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

// Per-product stock summary (status-bucket batch counts + total quantity + nearest expiry)
// within a single zone of one location, for the shelf-builder canvas's per-box status dot and
// hover popover (TASK-716, extended for the hover-detail request). Filters server-side with
// zone_id (not just store_id) — a single zone's batch count is far less likely to brush the
// backend's 200-row page clamp than the whole location's, unlike useZoneStatusCounts above
// (which genuinely needs every zone in one page and has no narrower filter to lean on). Kept as
// a separate query (distinct queryKey/queryFn) rather than deriving from useZoneStatusCounts's
// cache entry — the two hooks group the same rows differently and are used by different pages.
export function useZoneItemStatusCounts(locationId: string | null, zoneId: string | null) {
  return useQuery({
    queryKey: ["locations", locationId, "zone-item-status", zoneId],
    queryFn: async () => {
      const page = await stockApi.getAll({ store_id: locationId!, zone_id: zoneId!, pageSize: 200 });
      const byProduct = new Map<string, ZoneItemSummary>();
      for (const b of page.items) {
        if (b.storeId !== locationId || b.zoneId !== zoneId || b.quantity <= 0) continue;
        let summary = byProduct.get(b.productId);
        if (!summary) {
          summary = {
            counts: { safe: 0, warning: 0, critical: 0, expired: 0 },
            totalQuantity: 0,
            nearestExpiryDays: null,
            nearestExpiryDate: null,
          };
          byProduct.set(b.productId, summary);
        }
        if (b.status in summary.counts) summary.counts[b.status as keyof ZoneStatusCounts]++;
        summary.totalQuantity += b.quantity;
        if (summary.nearestExpiryDays === null || b.daysLeft < summary.nearestExpiryDays) {
          summary.nearestExpiryDays = b.daysLeft;
          summary.nearestExpiryDate = b.expiryDate;
        }
      }
      return byProduct;
    },
    enabled: !!locationId && !!zoneId,
  });
}
