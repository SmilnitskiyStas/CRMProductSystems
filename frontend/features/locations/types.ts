export type LocationType =
  | "retail_store"
  | "warehouse"
  | "auto_service"
  | "office"
  | "production"
  | "restaurant";

export const LOCATION_TYPE_LABELS: Record<LocationType, string> = {
  retail_store: "Роздрібний магазин",
  warehouse: "Склад",
  auto_service: "Автосервіс",
  office: "Офіс",
  production: "Виробництво",
  restaurant: "Ресторан",
};

export interface LocationZoneDto {
  id: string;
  locationId: string;
  name: string;
  type: string;
  position: string | null;
  shelvesCount: number;
  tempMin: number | null;
  tempMax: number | null;
  isActive: boolean;
}

// Layout stored in locations.floor_plan (jsonb). Zones not listed here are "unplaced".
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
  canvasW: number;
  canvasH: number;
  zones: FloorPlanZonePlacement[];
}

export interface ShelfItemPlacement {
  shelfId: string;
  label: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface ShelfPlanLayout {
  version: 1;
  grid: number;
  canvasW: number;
  canvasH: number;
  items: ShelfItemPlacement[];
}

export type ZoneStatus = "safe" | "warning" | "critical" | "expired";

export interface ZoneStatusCounts {
  safe: number;
  warning: number;
  critical: number;
  expired: number;
}

export interface LocationDto {
  id: string;
  name: string;
  address: string | null;
  latitude: number | null;
  longitude: number | null;
  locationType: LocationType;
  floorPlan: string | null;
  isActive: boolean;
  createdAt: string;
  zones: LocationZoneDto[];
}
