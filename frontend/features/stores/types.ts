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
