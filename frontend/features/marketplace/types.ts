// ─── Marketplace Feature Types ────────────────────────────────────────────────
// Matches API DTOs from TASK-221 (Marketplace API)

export type SupplierPlan = "free" | "premium";

export interface SupplierProfileDto {
  id: string;
  tenantId: string;
  companyName: string;
  region: string;
  categories: string[];
  plan: SupplierPlan;
  isPublic: boolean;
  /** Premium-only fields — null when plan=free or caller lacks auth */
  website: string | null;
  deliveryRegions: string[];
  workingHours: string | null;
  paymentTerms: string | null;
  /** Metrics */
  rating: number | null;
  avgDeliveryDays: number | null;
  orderAccuracy: number | null;
  qualityScore: number | null;
  responseTimeHours: number | null;
  cancellationRate: number | null;
}

export interface SupplierItemDto {
  id: string;
  supplierId: string;
  itemId: string | null;
  customName: string;
  price: number;
  minQty: number;
  unit: string;
  isAvailable: boolean;
}

export interface SupplierReviewDto {
  id: string;
  supplierId: string;
  tenantId: string;
  rating: number;
  comment: string | null;
  createdAt: string;
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
}
