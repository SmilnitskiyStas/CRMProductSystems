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

/** Image of a supplier item. Ordered by sortOrder; kind "main" is the cover image. */
export interface CabinetItemImage {
  url: string;
  kind: "main" | "gallery";
  sortOrder: number;
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
  images: CabinetItemImage[];
}

/** POST /api/supplier-cabinet/items */
export interface CabinetAddItemRequest {
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
  barcodes?: string[];
  imageUrls?: string[];
}

/** PUT /api/supplier-cabinet/items/{id} — patch semantics; barcodes/imageUrls
 * are only replaced when explicitly sent (undefined = leave untouched). */
export interface CabinetUpdateItemRequest {
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

/** Review as returned by GET /api/supplier-cabinet/reviews (reviewer by display name). */
export interface CabinetReview {
  id: string;
  rating: number;
  comment: string | null;
  createdAt: string;
  reviewerName: string;
  replyText: string | null;
  repliedAt: string | null;
}

/** PUT /api/supplier-cabinet/reviews/{id}/reply */
export interface CabinetReplyToReviewRequest {
  replyText: string;
}

/** GET /api/supplier-cabinet/reviews/stats */
export interface SupplierReviewStats {
  positive: number;
  neutral: number;
  negative: number;
  total: number;
  averageRating: number | null;
}

/** Backend PagedResult<T>. */
export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

/** POST /api/supplier-cabinet/staff — invite a new staff member for the supplier tenant. */
export interface CabinetInviteStaffRequest {
  email: string;
  fullName: string;
  password: string;
  /** Optional — omit for full access (old behavior, unchanged). */
  supplierRoleId?: string;
}

// ─── Supplier roles (TASK-307, Part 3) ─────────────────────────────────────────

/** GET/POST/PUT /api/supplier-cabinet/roles */
export interface SupplierRoleDto {
  id: string;
  displayName: string;
  /** Always "supplier_admin" for now — no other supplier base role exists. */
  baseRole: string;
  /** Subset of SUPPLIER_PERMISSIONS keys (see lib/supplierPermissions.ts). */
  permissions: string[];
  isSystem: boolean;
}

export interface CreateSupplierRoleRequest {
  displayName: string;
  baseRole: string;
  permissions: string[];
}

export type UpdateSupplierRoleRequest = CreateSupplierRoleRequest;

// ─── Supplier task board (TASK-307, Part 4) ────────────────────────────────────

export type SupplierTaskStatus = "pending" | "in_progress" | "completed" | "cancelled";

/** GET/POST/PUT /api/supplier-cabinet/tasks */
export interface SupplierTaskDto {
  id: string;
  clientTenantId: string | null;
  clientTenantName: string | null;
  assignedToUserId: string | null;
  assignedToUserName: string | null;
  title: string;
  description: string | null;
  status: SupplierTaskStatus;
  dueDate: string | null;
  createdByUserId: string | null;
  createdAt: string;
  completedAt: string | null;
}

export interface CreateSupplierTaskRequest {
  title: string;
  description?: string | null;
  clientTenantId?: string | null;
  assignedToUserId?: string | null;
  dueDate?: string | null;
}

export type UpdateSupplierTaskRequest = CreateSupplierTaskRequest;

export interface UpdateSupplierTaskStatusRequest {
  status: SupplierTaskStatus;
}

/** Query params for GET /api/supplier-cabinet/tasks */
export interface SupplierTaskFilters {
  assignedToMe?: boolean;
  clientTenantId?: string;
  status?: SupplierTaskStatus;
}

// ─── Clients tab (TASK-314, Частина 1) ─────────────────────────────────────────

/** GET /api/supplier-cabinet/clients — union of reviewers + task clients. */
export interface SupplierClientDto {
  tenantId: string;
  tenantName: string;
  reviewCount: number;
  avgRating: number | null;
  taskCount: number;
  lastInteractionAt: string;
}

// ─── Supplier ↔ client chat (TASK-314, Частина 2) ──────────────────────────────

export interface SupplierChatSessionDto {
  id: string;
  otherTenantId: string;
  otherTenantName: string;
  createdAt: string;
  updatedAt: string;
  lastMessage: string | null;
  lastMessageAt: string | null;
}

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

/** POST body for sending a message on either side of the supplier↔client chat. */
export interface SendSupplierChatMessageRequest {
  body: string;
}

// ─── Cooperation flow — contract settings (TASK-318) ───────────────────────────
// Shared cooperation DTO shapes (CooperationAgreementDto, MarketplaceOrderDto,
// SupplierSupportTicketDto, SupportTicketMessageDto, статуси) живуть у
// features/marketplace/types.ts і імпортуються звідти напряму — без дублювання.
// Тут лише supplier-only реквізити договору (CooperationDtos.cs).

/** GET /api/supplier-cabinet/contract-settings (404 until first saved). */
export interface SupplierContractSettingsDto {
  legalName: string;
  edrpou: string | null;
  iban: string | null;
  bankName: string | null;
  legalAddress: string | null;
  directorName: string | null;
  phone: string | null;
  email: string | null;
  serviceName: string | null;
  serviceDescription: string | null;
  signatureImageUrl: string | null;
  stampImageUrl: string | null;
  isVatPayer: boolean;
  updatedAt: string;
}

/** PUT /api/supplier-cabinet/contract-settings — full replace; images uploaded separately. */
export interface UpsertContractSettingsRequest {
  legalName: string;
  edrpou?: string | null;
  iban?: string | null;
  bankName?: string | null;
  legalAddress?: string | null;
  directorName?: string | null;
  phone?: string | null;
  email?: string | null;
  serviceName?: string | null;
  serviceDescription?: string | null;
  isVatPayer: boolean;
}

/** POST /api/supplier-cabinet/orders/{id}/status — Reason обовʼязковий для cancelled. */
export interface UpdateMarketplaceOrderStatusRequest {
  status: "confirmed" | "shipped" | "delivered" | "cancelled";
  reason?: string;
}
