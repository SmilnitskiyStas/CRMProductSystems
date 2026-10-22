import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
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
  ProductionRequestListItemDto,
  ProductionRequestDetailDto,
  ProductionRequestFilters,
  ProductionRequestCreateDto,
  ProductionRequestUpdateDto,
  ProductionRequestReviewDto,
  ProductionRequestSummaryItemDto,
  OrdersFromRequestsDto,
  OrderFromRequestResultDto,
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

  /** POST /api/production/orders/{id}/start */
  startOrder: (id: string) =>
    api.post<ProductionOrderDetailDto>(`/api/production/orders/${id}/start`),

  /** POST /api/production/orders/{id}/complete */
  completeOrder: (id: string) =>
    api.post<ProductionCompleteResultDto>(
      `/api/production/orders/${id}/complete`
    ),

  /** POST /api/production/orders/{id}/cancel */
  cancelOrder: (id: string) =>
    api.post<void>(`/api/production/orders/${id}/cancel`),

  /** POST /api/production/orders/from-requests */
  createOrdersFromRequests: (body: OrdersFromRequestsDto) =>
    api.post<OrderFromRequestResultDto[]>(
      "/api/production/orders/from-requests",
      body
    ),

  // ── Store requests ─────────────────────────────────────────────────────────

  /** GET /api/production-requests */
  getRequests: (f: ProductionRequestFilters = {}) => {
    const qs = new URLSearchParams();
    for (const [k, v] of Object.entries(f)) if (v) qs.set(k, v);
    const q = qs.toString();
    return api.get<ProductionRequestListItemDto[]>(
      `/api/production-requests${q ? `?${q}` : ""}`
    );
  },

  getRequest: (id: string) =>
    api.get<ProductionRequestDetailDto>(`/api/production-requests/${id}`),

  createRequest: (body: ProductionRequestCreateDto) =>
    api.post<ProductionRequestDetailDto>("/api/production-requests", body),

  updateRequest: (id: string, body: ProductionRequestUpdateDto) =>
    api.put<ProductionRequestDetailDto>(`/api/production-requests/${id}`, body),

  submitRequest: (id: string) =>
    api.post<ProductionRequestDetailDto>(`/api/production-requests/${id}/submit`),

  /** POST /api/production-requests/auto-generate — creates/updates auto drafts. */
  autoGenerateRequests: (storeLocationId?: string) =>
    api.post<ProductionRequestDetailDto[]>("/api/production-requests/auto-generate", {
      storeLocationId: storeLocationId || undefined,
    }),

  cancelRequest: (id: string) =>
    api.post<ProductionRequestDetailDto>(`/api/production-requests/${id}/cancel`),

  /** Production module only. */
  getRequestSummary: (productionLocationId?: string, status?: string) => {
    const qs = new URLSearchParams();
    if (productionLocationId) qs.set("productionLocationId", productionLocationId);
    if (status) qs.set("status", status);
    const q = qs.toString();
    return api.get<ProductionRequestSummaryItemDto[]>(
      `/api/production-requests/summary${q ? `?${q}` : ""}`
    );
  },

  reviewRequest: (id: string, body: ProductionRequestReviewDto) =>
    api.post<ProductionRequestDetailDto>(`/api/production-requests/${id}/review`, body),

  rejectRequest: (id: string, reason: string) =>
    api.post<ProductionRequestDetailDto>(`/api/production-requests/${id}/reject`, { reason }),

  shipRequest: (id: string) =>
    api.post<ProductionRequestDetailDto>(`/api/production-requests/${id}/ship`),

  // ── Selectors ─────────────────────────────────────────────────────────────

  /** GET /api/items — for item selector dropdowns */
  getItems: () =>
    api
      .get<PagedResult<ItemSlimDto>>("/api/items")
      .then((r) => r.items),

  /** GET /api/locations — for location selector dropdown */
  getLocations: () => api.get<LocationDto[]>("/api/locations"),
};
