export type LocationType =
  | "retail_store"
  | "warehouse"
  | "auto_service"
  | "office"
  | "production"
  | "restaurant";

// Client-side-only sort key for the Locations page's already-fetched-full-list table
// (no pagination on this page, so no backend sort contract — see TASK-628-frontend brief).
export type LocationSortKey = "name" | "type";

// Display labels moved to i18n messages under `Dashboard.locations.types` (i18n Block 2b,
// TASK-380) — this Record<LocationType, string> is intentionally gone. Components render
// the label via `useTranslations("Dashboard.locations.types")` keyed by the type value
// itself (e.g. `t("retail_store")`).
export const LOCATION_TYPE_VALUES: LocationType[] = [
  "retail_store",
  "warehouse",
  "auto_service",
  "office",
  "production",
  "restaurant",
];

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
  /** Linked catalog product (TASK-716). Optional so plans saved before this feature keep
   * loading unchanged — absent/null means "no product linked, free-text label only". */
  itemId?: string | null;
  /** Whether this section has a position on the canvas. Optional so plans saved before this
   * flag existed keep rendering unchanged — absent means placed (`true`). A freshly-created
   * section starts as `false` (exists in the Sections list only, not drawn on the canvas)
   * until explicitly placed, so "Add section" no longer keeps stacking new boxes below the
   * last one and forcing a long scroll. */
  placed?: boolean;
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

/**
 * Per-product stock summary within one zone, for the shelf-builder canvas's hover popover.
 * `counts` is batch counts per status (same shape/semantics as `ZoneStatusCounts` elsewhere —
 * batches, not units). `totalQuantity` sums `quantity` across every batch. `nearestExpiryDays`/
 * `nearestExpiryDate` describe the single soonest-expiring batch (the one driving `worstStatus`
 * most of the time); both null when there is no stock.
 */
export interface ZoneItemSummary {
  counts: ZoneStatusCounts;
  totalQuantity: number;
  nearestExpiryDays: number | null;
  nearestExpiryDate: string | null;
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
  /** Юридична особа мережі, до якої прив'язана локація (nullable). */
  legalEntityId: string | null;
  /** Структурований код регіону України (ISO 3166-2:UA область або місто), nullable (TASK-658). */
  regionCode: string | null;
}
