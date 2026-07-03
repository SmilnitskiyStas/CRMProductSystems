// ─── Supplier Cabinet Feature Types (v4.1, TASK-286, ADR-016) ─────────────────
// Matches backend DTOs: ShelfGuard.Application/Features/Marketplace/Dtos/MarketplaceDtos.cs
// (SupplierProfileDto, SupplierItemDto, PublicSupplierReviewDto, SupplierMetricsDto).

export type SupplierPlan = "free" | "premium";

export interface CabinetMetrics {
  rating: number | null;
  avgDeliveryDays: number | null;
  orderAccuracy: number | null;
  qualityScore: number | null;
  cancellationRate: number | null;
  responseTimeHours: number | null;
  updatedAt: string;
}

/** GET /api/supplier-cabinet/profile — own profile with metrics. */
export interface CabinetProfile {
  supplierId: string;
  supplierName: string;
  region: string | null;
  categories: string[] | null;
  website: string | null;
  deliveryRegions: string[] | null;
  workingHours: string | null;
  paymentTerms: string | null;
  isPublic: boolean;
  plan: SupplierPlan;
  metrics: CabinetMetrics | null;
}

/** PUT /api/supplier-cabinet/profile — patch semantics, only non-null fields applied. */
export interface CabinetProfileUpdateRequest {
  region?: string;
  categories?: string[];
  website?: string;
  deliveryRegions?: string[];
  workingHours?: string;
  paymentTerms?: string;
}

/** Item of the own catalog (includes unavailable items). */
export interface CabinetItem {
  id: string;
  itemId: string | null;
  customName: string | null;
  itemName: string | null;
  price: number | null;
  minQty: number | null;
  unit: string | null;
  isAvailable: boolean;
}

/** POST /api/supplier-cabinet/items */
export interface CabinetAddItemRequest {
  customName: string;
  price?: number;
  minQty?: number;
  unit?: string;
  isAvailable: boolean;
}

/** PUT /api/supplier-cabinet/items/{id} — patch semantics. */
export interface CabinetUpdateItemRequest {
  customName?: string;
  price?: number;
  minQty?: number;
  unit?: string;
  isAvailable?: boolean;
}

/** Review as returned by GET /api/supplier-cabinet/reviews (reviewer by display name). */
export interface CabinetReview {
  id: string;
  rating: number;
  comment: string | null;
  createdAt: string;
  reviewerName: string;
}

/** Backend PagedResult<T>. */
export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
