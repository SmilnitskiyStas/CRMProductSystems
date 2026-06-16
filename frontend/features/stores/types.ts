// Re-export aliases for backward compatibility — all consumers of features/stores
// should migrate to features/locations. These aliases keep existing imports working.
export type {
  LocationZoneDto as StoreZoneDto,
  LocationDto as StoreDto,
  FloorPlanZonePlacement,
  FloorPlanLayout,
  ZoneStatus,
  ZoneStatusCounts,
} from "@/features/locations/types";
