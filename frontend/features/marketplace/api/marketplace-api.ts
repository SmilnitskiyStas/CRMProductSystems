import { api } from "@/lib/api";
import type {
  SupplierProfileDto,
  SupplierItemDto,
  SupplierReviewDto,
  PaginatedResponse,
  MarketplaceSearchRequest,
  CreateReviewRequest,
  SupplierProfileUpdateRequest,
  SupplierPlan,
  CreateSupplierRequest,
  AddSupplierItemRequest,
} from "../types";

export const marketplaceApi = {
  /** GET /api/marketplace/suppliers — paginated listing with optional filters */
  getSuppliers: (params: {
    page?: number;
    pageSize?: number;
    region?: string;
    category?: string;
    plan?: "all" | SupplierPlan;
  }) => {
    const qs = new URLSearchParams();
    qs.set("page", String(params.page ?? 1));
    qs.set("pageSize", String(params.pageSize ?? 20));
    if (params.region) qs.set("region", params.region);
    if (params.category) qs.set("category", params.category);
    if (params.plan && params.plan !== "all") qs.set("plan", params.plan);
    return api.get<PaginatedResponse<SupplierProfileDto>>(
      `/api/marketplace/suppliers?${qs.toString()}`
    );
  },

  /** GET /api/marketplace/suppliers/{id} — supplier profile */
  getSupplier: (id: string) =>
    api.get<SupplierProfileDto>(`/api/marketplace/suppliers/${id}`),

  /** GET /api/marketplace/suppliers/{id}/items — supplier catalog */
  getSupplierItems: (supplierId: string) =>
    api.get<SupplierItemDto[]>(`/api/marketplace/suppliers/${supplierId}/items`),

  /** GET /api/marketplace/suppliers/{id}/reviews */
  getSupplierReviews: (supplierId: string) =>
    api.get<SupplierReviewDto[]>(`/api/marketplace/suppliers/${supplierId}/reviews`),

  /** POST /api/marketplace/search */
  search: (body: MarketplaceSearchRequest) =>
    api.post<SupplierProfileDto[]>("/api/marketplace/search", body),

  /** POST /api/marketplace/suppliers/{id}/reviews */
  createReview: (supplierId: string, body: CreateReviewRequest) =>
    api.post<SupplierReviewDto>(
      `/api/marketplace/suppliers/${supplierId}/reviews`,
      body
    ),

  /** GET /api/settings/supplier-profile */
  getMyProfile: () =>
    api.get<SupplierProfileUpdateRequest>("/api/settings/supplier-profile"),

  /** PUT /api/settings/supplier-profile */
  updateMyProfile: (body: SupplierProfileUpdateRequest) =>
    api.put<SupplierProfileDto>("/api/settings/supplier-profile", body),

  // ── Admin / platform endpoints (TASK-275) ─────────────────────────────────

  /** POST /api/admin/marketplace/suppliers */
  adminCreateSupplier: (body: CreateSupplierRequest) =>
    api.post<SupplierProfileDto>("/api/admin/marketplace/suppliers", body),

  /** POST /api/admin/marketplace/suppliers/{id}/items */
  adminAddSupplierItem: (supplierId: string, body: AddSupplierItemRequest) =>
    api.post<SupplierItemDto>(
      `/api/admin/marketplace/suppliers/${supplierId}/items`,
      body
    ),

  /** DELETE /api/admin/marketplace/suppliers/{id}/items/{itemId} */
  adminDeleteSupplierItem: (supplierId: string, itemId: string) =>
    api.delete<void>(
      `/api/admin/marketplace/suppliers/${supplierId}/items/${itemId}`
    ),
};
