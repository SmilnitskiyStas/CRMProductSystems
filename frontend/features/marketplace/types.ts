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

/** Aggregated public review stats (GET reviewStats on supplier profile). */
export interface SupplierReviewStats {
  positive: number;
  neutral: number;
  negative: number;
  total: number;
  averageRating: number | null;
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
  reviewStats?: SupplierReviewStats | null;
}

/** Image of a supplier item. Ordered by sortOrder; kind "main" is the cover image. */
export interface SupplierItemImageDto {
  url: string;
  kind: "main" | "gallery";
  sortOrder: number;
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
  brand: string | null;
  manufacturer: string | null;
  manufacturerCountry: string | null;
  maxQty: number | null;
  grossWeightKg: number | null;
  heightCm: number | null;
  depthCm: number | null;
  widthCm: number | null;
  /** Ordered primary-first; first entry is the primary barcode, rest are alternates. */
  barcodes: string[];
  /** Ordered by sortOrder; kind "main" is the cover image. */
  images: SupplierItemImageDto[];
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
  replyText: string | null;
  repliedAt: string | null;
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

export interface AddSupplierItemRequest {
  customName: string;
  price?: number;
  minQty?: number;
  unit?: string;
  isAvailable: boolean;
  category?: string;
  attributes?: Record<string, unknown>;
  brand?: string;
  manufacturer?: string;
  manufacturerCountry?: string;
  maxQty?: number;
  grossWeightKg?: number;
  heightCm?: number;
  depthCm?: number;
  widthCm?: number;
  /** First = primary barcode, rest = alternates. */
  barcodes?: string[];
  /** First = main image, rest = gallery. */
  imageUrls?: string[];
}

// ─── Supplier ↔ client chat, client side (TASK-314, Частина 2) ────────────────
// Same shapes as the supplier-side ones in features/supplier-cabinet/types.ts.

export interface SupplierChatMessageDto {
  id: string;
  sessionId: string;
  senderTenantId: string;
  senderUserId: string;
  senderName: string;
  body: string;
  isRead: boolean;
  createdAt: string;
}

export interface SendSupplierChatMessageRequest {
  body: string;
}

// ─── Cooperation agreements / marketplace orders / support tickets (TASK-318) ──
// Matches backend DTOs: ShelfGuard.Application/Features/Marketplace/Dtos/CooperationDtos.cs
// (handoff .claude/logs/handoffs/317-to-318_frontend-developer.md).

export type CooperationStatus =
  | "pending"
  | "rejected"
  | "awaiting_signature"
  | "active"
  | "terminated";

export interface CooperationAgreementDto {
  id: string;
  supplierTenantId: string;
  clientTenantId: string;
  supplierName: string;
  clientName: string;
  status: CooperationStatus;
  requestMessage: string | null;
  /** Причина відмови АБО причина розірвання. */
  rejectionReason: string | null;
  /** «ДС-2026-001» */
  contractNumber: string | null;
  hasContractFile: boolean;
  vchasnoDocumentId: string | null;
  requestedAt: string;
  decidedAt?: string | null;
  signedAt?: string | null;
  terminatedAt?: string | null;
}

export interface CreateCooperationRequestBody {
  message?: string;
}

export type MarketplaceOrderStatus =
  | "new"
  | "confirmed"
  | "shipped"
  | "delivered"
  | "cancelled";

/** Line item snapshot at order time. */
export interface MarketplaceOrderItemDto {
  id: string;
  supplierItemId: string | null;
  itemName: string;
  unit: string | null;
  price: number;
  qty: number;
  lineTotal: number;
}

export interface MarketplaceOrderDto {
  id: string;
  /** «MP-2026-001» */
  orderNumber: string;
  agreementId: string;
  supplierTenantId: string;
  clientTenantId: string;
  supplierName: string;
  clientName: string;
  status: MarketplaceOrderStatus;
  comment: string | null;
  cancelReason: string | null;
  totalAmount: number;
  createdAt: string;
  updatedAt: string;
  items: MarketplaceOrderItemDto[];
}

export interface CreateMarketplaceOrderRequest {
  items: { supplierItemId: string; qty: number }[];
  comment?: string;
}

export type SupportTicketStatus = "open" | "in_progress" | "resolved" | "closed";

export interface SupportTicketMessageDto {
  id: string;
  ticketId: string;
  senderTenantId: string;
  senderUserId: string;
  body: string;
  isRead: boolean;
  createdAt: string;
}

export interface SupplierSupportTicketDto {
  id: string;
  supplierTenantId: string;
  clientTenantId: string;
  supplierName: string;
  clientName: string;
  subject: string;
  status: SupportTicketStatus;
  createdAt: string;
  updatedAt: string;
  /** null у списках; заповнено в GET одного тікета (старіші перші). */
  messages: SupportTicketMessageDto[] | null;
}

export interface CreateSupportTicketRequest {
  subject: string;
  message: string;
}

/** Patch-semantics update — barcodes/imageUrls are only replaced when explicitly sent. */
export interface UpdateSupplierItemRequest {
  customName?: string;
  price?: number;
  minQty?: number;
  unit?: string;
  isAvailable?: boolean;
  category?: string;
  attributes?: Record<string, unknown>;
  brand?: string;
  manufacturer?: string;
  manufacturerCountry?: string;
  maxQty?: number;
  grossWeightKg?: number;
  heightCm?: number;
  depthCm?: number;
  widthCm?: number;
  barcodes?: string[];
  imageUrls?: string[];
}
