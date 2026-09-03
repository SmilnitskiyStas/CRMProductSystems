// ─── Supplier Cabinet Feature Types (v4.1, TASK-286, ADR-016) ─────────────────
// Matches backend DTOs: ShelfGuard.Application/Features/Marketplace/Dtos/MarketplaceDtos.cs
// (SupplierProfileDto, SupplierItemDto, PublicSupplierReviewDto, SupplierMetricsDto).

import type { DeliveryCoverage } from "@/features/geo/types";
import type {
  MarketplaceOrderDto,
  SupplierMetricsHistoryPoint,
} from "@/features/marketplace/types";

export type SupplierPlan = "free" | "premium";

export interface CabinetMetrics {
  rating: number | null;
  avgDeliveryDays: number | null;
  orderAccuracy: number | null;
  qualityScore: number | null;
  cancellationRate: number | null;
  responseTimeHours: number | null;
  updatedAt: string;
  // TASK-689 (Phase 6d): worker-computed composite quality score + on-time delivery rate
  // (both 0..1). Null until the nightly job has components / a delivered sample.
  compositeScore?: number | null;
  onTimeDeliveryRate?: number | null;
}

/** GET /api/supplier-cabinet/profile — own profile with metrics. */
export interface CabinetProfile {
  supplierId: string;
  supplierName: string;
  region: string | null;
  categories: string[] | null;
  website: string | null;
  /** Legacy free-text list — deprecated, still surfaced until the T14 backfill (TASK-655). */
  deliveryRegions: string[] | null;
  workingHours: string | null;
  paymentTerms: string | null;
  isPublic: boolean;
  plan: SupplierPlan;
  metrics: CabinetMetrics | null;
  /** Structured delivery coverage (TASK-655) — NOT premium-gated. */
  deliveryCoverage?: DeliveryCoverage | null;
}

/** PUT /api/supplier-cabinet/profile — patch semantics, only non-null fields applied. */
export interface CabinetProfileUpdateRequest {
  region?: string;
  categories?: string[];
  website?: string;
  workingHours?: string;
  paymentTerms?: string;
  /** Structured delivery coverage (TASK-655). Omit / null leaves the stored value untouched. */
  deliveryCoverage?: DeliveryCoverage | null;
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
  /** Legacy attribute-schema key (SupplierItemCategories registry). Independent of the browse
   *  taxonomy below. */
  category: string | null;
  /** Browse-taxonomy link into platform_categories (supplier-portal expansion #8, Phase 6e).
   *  Name resolved on read; null when unset. */
  platformCategoryId: string | null;
  platformCategoryName: string | null;
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
  /** Browse-taxonomy link (Phase 6e). Must be an existing, active platform_categories row. */
  platformCategoryId?: string;
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
  /** Browse-taxonomy link (Phase 6e). Patch semantics: omit / undefined — leave untouched;
   *  the all-zero guid `"00000000-0000-0000-0000-000000000000"` — clear it; any other value —
   *  set it (validated existing + active). */
  platformCategoryId?: string;
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
  unreadCount: number;
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

/**
 * POST /api/supplier-cabinet/orders/{id}/status — Reason обовʼязковий для
 * cancelled; estimatedDeliveryDays обовʼязковий (> 0) для shipped — backend
 * повертає 400 "Вкажіть орієнтовну кількість днів до доставки." інакше.
 */
export interface UpdateMarketplaceOrderStatusRequest {
  status: "confirmed" | "shipped" | "delivered" | "cancelled";
  reason?: string;
  estimatedDeliveryDays?: number;
}

/** POST /api/supplier-cabinet/orders/{id}/delay-reason — reason required, non-blank
 * (trimmed server-side); only allowed while order.status === "shipped" (TASK-585). */
export interface SetOrderDelayReasonRequest {
  reason: string;
}

/**
 * POST /api/supplier-cabinet/orders/{id}/expected-delivery-date — supplier reschedules a
 * shipped order's expected delivery date (supplier-portal expansion Phase 4, plan D5).
 * Repeatable while order.status === "shipped" (no "already set" guard); the date must not be
 * in the past. 400 { error } (order not shipped / date in the past), 404 unknown/foreign.
 */
export interface SetOrderExpectedDeliveryDateRequest {
  expectedDeliveryDate: string; // "YYYY-MM-DD"
}

// ── Warehouses (supplier-portal expansion, plan phase 1) ─────────────────────
// A supplier "warehouse" is a Location row (type "warehouse"). Gated by the
// provider-granted "supplier_inventory" module + "warehouse_management" permission.

export interface SupplierWarehouse {
  id: string;
  name: string;
  address: string | null;
  /** ISO 3166-2:UA region code (same picker as the supplier profile / delivery coverage). */
  regionCode: string | null;
  isActive: boolean;
}

/** POST /api/supplier-cabinet/warehouses */
export interface CreateSupplierWarehouseRequest {
  name: string;
  address?: string | null;
  regionCode?: string | null;
}

/** PUT /api/supplier-cabinet/warehouses/{id} — full replace. */
export interface UpdateSupplierWarehouseRequest {
  name: string;
  address?: string | null;
  regionCode?: string | null;
  isActive: boolean;
}

// ── Warehouse batch inventory + receiving (supplier-portal expansion, Phase 2) ─
// Parallel to the retail Stock / Receipts model (decisions D2/D3): the supplier
// catalog is SupplierItem (nullable ItemId), and there is no zone / store-scope
// surface. Gated by "supplier_inventory" module + "warehouse_management" permission.
// Backend DTOs: ShelfGuard.Application/Features/SupplierInventory/Dtos/SupplierStockDtos.cs.

/** Batch status — same value set + chip colours as the retail shelf (StockStatus). */
export type SupplierStockStatus =
  | "safe"
  | "warning"
  | "critical"
  | "expired"
  | "sold_out"
  | "archived"
  | "needs_verification";

/**
 * One FEFO batch in a supplier warehouse.
 * GET /api/supplier-cabinet/warehouses/{warehouseId}/stock (paged, FEFO-ordered).
 */
export interface SupplierStock {
  id: string;
  supplierItemId: string;
  supplierItemName: string;
  warehouseId: string;
  warehouseName: string;
  expiryDate: string; // "YYYY-MM-DD"
  daysLeft: number;
  quantity: number;
  quantityInitial: number;
  batchNumber: string | null;
  status: SupplierStockStatus;
  sourceType: string | null;
  addedAt: string;
  lastCheckedAt: string;
}

export type SupplierStockReceiptStatus = "draft" | "received" | "cancelled";

/** One line of a supplier receipt — N rows may share a supplierItemId (one per batch). */
export interface SupplierStockReceiptItem {
  id: string;
  supplierItemId: string;
  supplierItemName: string;
  expiryDate: string | null; // "YYYY-MM-DD"; nullable while draft, required to finalize
  quantity: number;
  batchNumber: string | null;
  unitCost: number | null;
  notes: string | null;
}

/** GET/POST /api/supplier-cabinet/.../receipts — items sorted by expiry asc. */
export interface SupplierStockReceipt {
  id: string;
  warehouseId: string;
  warehouseName: string;
  status: SupplierStockReceiptStatus;
  reference: string | null;
  notes: string | null;
  receivedAt: string | null;
  createdAt: string;
  items: SupplierStockReceiptItem[];
}

/** POST /api/supplier-cabinet/warehouses/{warehouseId}/stock */
export interface AddSupplierBatchRequest {
  supplierItemId: string;
  expiryDate: string; // "YYYY-MM-DD" — must be in the future
  quantity: number;
  batchNumber?: string | null;
}

/** POST /api/supplier-cabinet/stock/{batchId}/adjust */
export interface AdjustSupplierStockRequest {
  quantity: number;
  reason?: string | null;
}

/** POST /api/supplier-cabinet/warehouses/{warehouseId}/receipts */
export interface CreateSupplierReceiptRequest {
  reference?: string | null;
  notes?: string | null;
}

/** PUT /api/supplier-cabinet/receipts/{id} — draft header only. */
export interface UpdateSupplierReceiptRequest {
  warehouseId: string;
  reference?: string | null;
  notes?: string | null;
}

/** POST /api/supplier-cabinet/receipts/{id}/lines */
export interface AddSupplierReceiptLineRequest {
  supplierItemId: string;
  expiryDate?: string | null; // "YYYY-MM-DD" — optional on a draft line
  quantity: number;
  batchNumber?: string | null;
  unitCost?: number | null;
  notes?: string | null;
}

// ── Batch-consuming shipment (supplier-portal expansion Phase 3, plan D4) ──────
// Only exercised when the provider-granted "supplier_inventory" module is on. With it
// off, CabinetOrdersTab keeps using UpdateMarketplaceOrderStatusRequest {status:"shipped"}.
// Backend DTOs: ShelfGuard.Application/Features/Marketplace/Dtos/CooperationDtos.cs.

/** One supplier_stock batch and how much of it goes onto an order line. */
export interface ShipAllocation {
  supplierStockId: string;
  qty: number;
}

/** Per-order-line allocation plan. An empty / omitted allocations list means "auto-FEFO
 *  this line" server-side; explicit allocations always win over auto-FEFO. */
export interface ShipLine {
  orderItemId: string;
  allocations?: ShipAllocation[];
}

/**
 * POST /api/supplier-cabinet/orders/{id}/ship — every field is optional so the legacy
 * {status:"shipped"} path maps onto the very same handler. At least one of
 * estimatedDeliveryDays (> 0) / expectedDeliveryDate is required; each derives the other.
 */
export interface ShipOrderRequest {
  sourceWarehouseId?: string | null;
  expectedDeliveryDate?: string | null; // "YYYY-MM-DD"
  estimatedDeliveryDays?: number | null;
  lines?: ShipLine[];
}

/** One proposed batch pick in a FEFO ship suggestion. */
export interface ShipSuggestionAllocation {
  supplierStockId: string;
  expiryDate: string; // "YYYY-MM-DD"
  batchNumber: string | null;
  /** Quantity currently on that batch — the editable cap for this pick. */
  available: number;
  /** Proposed quantity to take from this batch. */
  qty: number;
}

export interface ShipSuggestionLine {
  orderItemId: string;
  supplierItemId: string | null;
  itemName: string;
  unit: string | null;
  qty: number;
  /** Sum of the proposed allocations — less than qty when stock is short. */
  covered: number;
  shortfall: number;
  allocations: ShipSuggestionAllocation[];
}

/** GET /api/supplier-cabinet/orders/{id}/ship-suggestion?warehouseId= — editable FEFO plan. */
export interface ShipSuggestion {
  orderId: string;
  warehouseId: string | null;
  warehouseName: string | null;
  lines: ShipSuggestionLine[];
  warnings: string[];
}

/** POST .../ship result. warnings lists lines shipped short — NOT an error (the goods still
 *  ship; the uncovered qty arrives without batch data and the client types the expiry by hand). */
export interface ShipOrderResult {
  order: MarketplaceOrderDto;
  warnings: string[];
}

// ── Demand analytics (supplier-portal expansion #7, Phase 6b) ─────────────────
// GET /api/supplier-cabinet/analytics?from=YYYY-MM-DD&to=YYYY-MM-DD (default last 30d).
// Gated: "marketplace_supplier" module + "analytics_view" permission. Read-only, no
// cross-buyer leakage. Backend DTOs: ShelfGuard.Application/Features/SupplierAnalytics/Dtos.

/** Period-over-period movement (backend PeriodMetricDto). `percentChange` null when the
 *  preceding window was zero. */
export interface SupplierPeriodMetric {
  current: number;
  previous: number;
  percentChange: number | null;
}

/** One row in `topItems` / `slowItems`. */
export interface SupplierAnalyticsItem {
  /** Null when the order line's supplier catalog entry was deleted (FK SET NULL). */
  supplierItemId: string | null;
  itemName: string;
  qtySold: number;
  revenue: number;
  orderCount: number;
}

/** One buyer's slice of the window. */
export interface SupplierAnalyticsBuyer {
  clientTenantId: string;
  clientName: string;
  orderCount: number;
  revenue: number;
}

/** One day on the revenue trend line. */
export interface SupplierAnalyticsTrendPoint {
  /** "YYYY-MM-DD" */
  date: string;
  revenue: number;
  orderCount: number;
}

export interface SupplierAnalytics {
  /** Resolved window (may differ from the request when capped at 366 days). "YYYY-MM-DD". */
  from: string;
  to: string;
  totalRevenue: number;
  orderCount: number;
  itemsSold: number;
  revenueDelta: SupplierPeriodMetric;
  orderCountDelta: SupplierPeriodMetric;
  itemsSoldDelta: SupplierPeriodMetric;
  /** Up to 10, highest Σqty first. */
  topItems: SupplierAnalyticsItem[];
  /** Up to 10 of the available catalog with the least demand (zero-demand included), lowest first. */
  slowItems: SupplierAnalyticsItem[];
  /** Per buyer, highest revenue first. */
  byBuyer: SupplierAnalyticsBuyer[];
  /** Daily points, oldest first (chart-ready). */
  revenueTrend: SupplierAnalyticsTrendPoint[];
}

// ── Self-service metric history + period deltas (Phase 6c, request #9) ────────
// GET /api/supplier-cabinet/metrics-history?days=[7..365] (default 90). Gated:
// "marketplace_supplier" module + "client_reviews" permission. Mirrors the buyer-facing
// GET /api/marketplace/suppliers/{id}/metrics-history. Backend: SupplierMetricsHistoryResponseDto.

/** Period-over-period movement for one headline metric — null when either endpoint has no value. */
export interface SupplierMetricsHistoryDeltas {
  compositeScore: SupplierPeriodMetric | null;
  avgDeliveryDays: SupplierPeriodMetric | null;
  orderAccuracy: SupplierPeriodMetric | null;
  onTimeDeliveryRate: SupplierPeriodMetric | null;
  rating: SupplierPeriodMetric | null;
  responseTimeHours: SupplierPeriodMetric | null;
}

export interface SupplierMetricsHistoryResponse {
  /** Oldest → newest daily snapshots (chart-ready). Same shape as the buyer-facing point. */
  points: SupplierMetricsHistoryPoint[];
  /** Latest snapshot in the window vs. the oldest. */
  deltas: SupplierMetricsHistoryDeltas;
}
