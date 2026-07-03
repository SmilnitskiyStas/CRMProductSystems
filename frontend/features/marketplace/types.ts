// ─── Marketplace Feature Types ────────────────────────────────────────────────
// Matches backend DTOs (MarketplaceDtos.cs): SupplierListItemDto,
// SupplierProfileDto, SupplierMetricsDto, SupplierItemDto,
// PublicSupplierReviewDto (v4.1, TASK-285/287).

export type SupplierPlan = "free" | "premium";

/** Compact card in the public listing / search results. */
export interface SupplierListItemDto {
  id: string;
  name: string;
  region: string | null;
  plan: SupplierPlan;
  categories: string[] | null;
  rating: number | null;
  avgDeliveryDays: number | null;
  isPublic: boolean;
}

export interface SupplierMetricsDto {
  rating: number | null;
  avgDeliveryDays: number | null;
  orderAccuracy: number | null;
  qualityScore: number | null;
  cancellationRate: number | null;
  responseTimeHours: number | null;
  updatedAt: string;
}

/** Full supplier profile. Premium fields are null for unauthenticated/free callers. */
export interface SupplierProfileDto {
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
  metrics: SupplierMetricsDto | null;
}

export interface SupplierItemDto {
  id: string;
  itemId: string | null;
  customName: string | null;
  itemName: string | null;
  price: number | null;
  minQty: number | null;
  unit: string | null;
  isAvailable: boolean;
  category: string | null;
  attributes: Record<string, unknown> | null;
}

// ─── Item categories (ADR-017 §4, TASK-296) ───────────────────────────────────

export type SupplierItemFieldType = "text" | "number" | "date" | "bool" | "select";

export interface SupplierItemCategoryField {
  key: string;
  labelUa: string;
  type: SupplierItemFieldType;
  required: boolean;
  options: string[] | null;
}

export interface SupplierItemCategoryDto {
  key: string;
  labelUa: string;
  fields: SupplierItemCategoryField[];
}

/** Response of POST /suppliers/{id}/reviews. */
export interface SupplierReviewDto {
  id: string;
  rating: number;
  comment: string | null;
  createdAt: string;
}

/** Public review (GET /suppliers/{id}/reviews) — reviewer by display name only. */
export interface PublicSupplierReviewDto {
  id: string;
  rating: number;
  comment: string | null;
  createdAt: string;
  reviewerName: string;
}

// ─── Paginated list response ──────────────────────────────────────────────────

export interface PaginatedResponse<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

// ─── Request bodies ───────────────────────────────────────────────────────────

export interface MarketplaceSearchRequest {
  itemName: string;
  region?: string;
}

export interface CreateReviewRequest {
  rating: number;
  comment?: string;
}

export interface SupplierProfileUpdateRequest {
  region: string;
  categories: string[];
  website?: string;
  deliveryRegions: string[];
  workingHours?: string;
  paymentTerms?: string;
  isPublic: boolean;
  plan: SupplierPlan;
}

// ─── Filter state ─────────────────────────────────────────────────────────────

export interface MarketplaceFilters {
  region: string;
  category: string;
  plan: "all" | SupplierPlan;
}

// ─── Admin / platform request bodies (TASK-275) ───────────────────────────────

export interface CreateSupplierRequest {
  companyName: string;
  region?: string;
  categories?: string[];
  website?: string;
  deliveryRegions?: string[];
  workingHours?: string;
  paymentTerms?: string;
  isPublic: boolean;
  plan: SupplierPlan;
}

export interface AddSupplierItemRequest {
  customName: string;
  price?: number;
  minQty?: number;
  unit?: string;
  isAvailable: boolean;
  category?: string;
  attributes?: Record<string, unknown>;
}
