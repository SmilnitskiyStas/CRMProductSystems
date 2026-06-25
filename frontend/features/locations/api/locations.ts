import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type { LocationDto, LocationType } from "../types";

// Minimal stock shape needed for per-zone status counts on the floor plan
export interface StockBatchSlim {
  locationId: string;
  zoneId: string | null;
  status: string;
  quantity: number;
}

export interface CreateLocationDto {
  name: string;
  address?: string | null;
  locationType: LocationType;
}

export interface UpdateLocationDto {
  name: string;
  address?: string | null;
  locationType: LocationType;
  isActive: boolean;
}

export const locationsApi = {
  getAll: () => api.get<LocationDto[]>("/api/locations"),
  getById: (id: string) => api.get<LocationDto>(`/api/locations/${id}`),
  create: (data: CreateLocationDto) => api.post<LocationDto>("/api/locations", data),
  update: (id: string, data: UpdateLocationDto) =>
    api.put<LocationDto>(`/api/locations/${id}`, data),
  updateFloorPlan: (id: string, floorPlan: string) =>
    api.put<LocationDto>(`/api/locations/${id}/floor-plan`, { floorPlan }),
  getStock: () =>
    api
      .get<PagedResult<StockBatchSlim>>("/api/stock")
      .then((r) => r.items),
};
