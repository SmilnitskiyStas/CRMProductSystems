export interface StoreZoneDto {
  id: string;
  storeId: string;
  name: string;
  type: string;
  position: string | null;
  shelvesCount: number;
  tempMin: number | null;
  tempMax: number | null;
  isActive: boolean;
}

// Layout stored in stores.floor_plan (jsonb). Zones not listed here are "unplaced".
export interface FloorPlanZonePlacement {
  zoneId: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface FloorPlanLayout {
  version: 1;
  grid: number;
  zones: FloorPlanZonePlacement[];
}

export type ZoneStatus = "safe" | "warning" | "critical" | "expired";

export interface ZoneStatusCounts {
  safe: number;
  warning: number;
  critical: number;
  expired: number;
}

export interface StoreDto {
  id: string;
  name: string;
  address: string | null;
  latitude: number | null;
  longitude: number | null;
  type: string;
  floorPlan: string | null;
  isActive: boolean;
  createdAt: string;
  zones: StoreZoneDto[];
}
