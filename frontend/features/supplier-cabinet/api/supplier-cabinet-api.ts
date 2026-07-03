import { api } from "@/lib/api";
import type {
  CabinetProfile,
  CabinetProfileUpdateRequest,
  CabinetItem,
  CabinetAddItemRequest,
  CabinetUpdateItemRequest,
  CabinetReview,
  CabinetMetrics,
  PagedResult,
} from "../types";

const BASE = "/api/supplier-cabinet";

export const supplierCabinetApi = {
  /** GET /api/supplier-cabinet/profile */
  getProfile: () => api.get<CabinetProfile>(`${BASE}/profile`),

  /** PUT /api/supplier-cabinet/profile */
  updateProfile: (body: CabinetProfileUpdateRequest) =>
    api.put<CabinetProfile>(`${BASE}/profile`, body),

  /** POST /api/supplier-cabinet/profile/publish — toggles IsPublic */
  togglePublish: () => api.post<CabinetProfile>(`${BASE}/profile/publish`),

  /** GET /api/supplier-cabinet/items */
  getItems: () => api.get<CabinetItem[]>(`${BASE}/items`),

  /** POST /api/supplier-cabinet/items */
  addItem: (body: CabinetAddItemRequest) =>
    api.post<CabinetItem>(`${BASE}/items`, body),

  /** PUT /api/supplier-cabinet/items/{id} */
  updateItem: (id: string, body: CabinetUpdateItemRequest) =>
    api.put<CabinetItem>(`${BASE}/items/${id}`, body),

  /** DELETE /api/supplier-cabinet/items/{id} */
  deleteItem: (id: string) => api.delete<void>(`${BASE}/items/${id}`),

  /** GET /api/supplier-cabinet/reviews */
  getReviews: (page = 1, pageSize = 20) =>
    api.get<PagedResult<CabinetReview>>(
      `${BASE}/reviews?page=${page}&pageSize=${pageSize}`
    ),

  /** GET /api/supplier-cabinet/metrics */
  getMetrics: () => api.get<CabinetMetrics>(`${BASE}/metrics`),
};
