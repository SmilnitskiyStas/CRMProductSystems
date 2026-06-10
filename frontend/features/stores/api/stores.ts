import { api } from "@/lib/api";
import type { StoreDto } from "../types";

export const storesApi = {
  getAll: () => api.get<StoreDto[]>("/api/stores"),
  getById: (id: string) => api.get<StoreDto>(`/api/stores/${id}`),
};
