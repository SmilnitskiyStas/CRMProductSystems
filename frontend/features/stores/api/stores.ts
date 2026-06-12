import { api } from "@/lib/api";
import type { StoreDto } from "../types";

// Minimal stock shape needed for per-zone status counts on the floor plan
export interface StockBatchSlim {
  storeId: string;
  zoneId: string | null;
  status: string;
  quantity: number;
}

export const storesApi = {
  getAll: () => api.get<StoreDto[]>("/api/stores"),
  getById: (id: string) => api.get<StoreDto>(`/api/stores/${id}`),
  updateFloorPlan: (id: string, floorPlan: string) =>
    api.put<StoreDto>(`/api/stores/${id}/floor-plan`, { floorPlan }),
  getStock: () => api.get<StockBatchSlim[]>("/api/stock"),
};
