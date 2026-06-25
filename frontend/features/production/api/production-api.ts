import { api } from "@/lib/api";
import type {
  RecipeListItemDto,
  RecipeDetailDto,
  RecipeCreateDto,
  RecipeUpdateDto,
  ProductionOrderListItemDto,
  ProductionOrderDetailDto,
  ProductionOrderCreateDto,
  ProductionOrderUpdateDto,
  ProductionCompleteResultDto,
  ItemSlimDto,
} from "../types";
import type { LocationDto } from "@/features/locations/types";

export const productionApi = {
  // ── Recipes ────────────────────────────────────────────────────────────────

  /** GET /api/production/recipes */
  getRecipes: (includeInactive = false) => {
    const qs = includeInactive ? "?includeInactive=true" : "";
    return api.get<RecipeListItemDto[]>(`/api/production/recipes${qs}`);
  },

  /** GET /api/production/recipes/{id} */
  getRecipe: (id: string) =>
    api.get<RecipeDetailDto>(`/api/production/recipes/${id}`),

  /** POST /api/production/recipes */
  createRecipe: (body: RecipeCreateDto) =>
    api.post<RecipeDetailDto>("/api/production/recipes", body),

  /** PUT /api/production/recipes/{id} */
  updateRecipe: (id: string, body: RecipeUpdateDto) =>
    api.put<RecipeDetailDto>(`/api/production/recipes/${id}`, body),

  /** DELETE /api/production/recipes/{id} — soft-delete (deactivate) */
  deactivateRecipe: (id: string) =>
    api.delete<void>(`/api/production/recipes/${id}`),

  // ── Production Orders ──────────────────────────────────────────────────────

  /** GET /api/production/orders?status= */
  getOrders: (status?: string) => {
    const qs = status ? `?status=${status}` : "";
    return api.get<ProductionOrderListItemDto[]>(`/api/production/orders${qs}`);
  },

  /** GET /api/production/orders/{id} */
  getOrder: (id: string) =>
    api.get<ProductionOrderDetailDto>(`/api/production/orders/${id}`),

  /** POST /api/production/orders */
  createOrder: (body: ProductionOrderCreateDto) =>
    api.post<ProductionOrderDetailDto>("/api/production/orders", body),

  /** PUT /api/production/orders/{id} */
  updateOrder: (id: string, body: ProductionOrderUpdateDto) =>
    api.put<ProductionOrderDetailDto>(`/api/production/orders/${id}`, body),

  /** POST /api/production/orders/{id}/complete */
  completeOrder: (id: string) =>
    api.post<ProductionCompleteResultDto>(
      `/api/production/orders/${id}/complete`
    ),

  /** POST /api/production/orders/{id}/cancel */
  cancelOrder: (id: string) =>
    api.post<void>(`/api/production/orders/${id}/cancel`),

  // ── Selectors ─────────────────────────────────────────────────────────────

  /** GET /api/items — for item selector dropdowns */
  getItems: () =>
    api
      .get<{ items: ItemSlimDto[]; totalCount: number; page: number; pageSize: number }>("/api/items")
      .then((r) => r.items),

  /** GET /api/locations — for location selector dropdown */
  getLocations: () => api.get<LocationDto[]>("/api/locations"),
};
