import { api } from "@/lib/api";
import type { LocationDto, LocationType, LocationZoneDto } from "../types";

export interface CreateLocationDto {
  name: string;
  address?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  locationType: LocationType;
  legalEntityId?: string | null;
  regionCode?: string | null;
  timeZone?: string;
}

export interface UpdateLocationDto {
  name: string;
  address?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  locationType: LocationType;
  isActive: boolean;
  legalEntityId?: string | null;
  regionCode?: string | null;
  timeZone?: string;
}

export interface GeocodeAddressResult {
  latitude: number;
  longitude: number;
  displayName: string;
}

export interface CreateZoneDto {
  name: string;
  type: string;
  shelvesCount: number;
  tempMin?: number | null;
  tempMax?: number | null;
}

export const locationsApi = {
  getAll: () => api.get<LocationDto[]>("/api/locations"),
  getById: (id: string) => api.get<LocationDto>(`/api/locations/${id}`),
  create: (data: CreateLocationDto) => api.post<LocationDto>("/api/locations", data),
  update: (id: string, data: UpdateLocationDto) =>
    api.put<LocationDto>(`/api/locations/${id}`, data),
  geocode: (query: string) =>
    api.post<GeocodeAddressResult>("/api/locations/geocode", { query }),
  updateFloorPlan: (id: string, floorPlan: string) =>
    api.put<LocationDto>(`/api/locations/${id}/floor-plan`, { floorPlan }),
  uploadFloorPlanBackground: (id: string, file: Blob, fileName = "plan.png") => {
    const form = new FormData();
    form.append("file", file, fileName);
    return api.postForm<{ url: string }>(`/api/locations/${id}/floor-plan/background`, form);
  },
  createZone: (locationId: string, data: CreateZoneDto) =>
    api.post<LocationZoneDto>(`/api/locations/${locationId}/zones`, data),
  updateZone: (locationId: string, zoneId: string, data: Partial<CreateZoneDto> & { isActive?: boolean; name?: string; position?: string }) =>
    api.put<LocationZoneDto>(`/api/locations/${locationId}/zones/${zoneId}`, data),
};
