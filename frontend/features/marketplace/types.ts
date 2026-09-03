// ─── Marketplace Feature Types ────────────────────────────────────────────────
// Matches backend DTOs (MarketplaceDtos.cs): SupplierListItemDto,
// SupplierProfileDto, SupplierMetricsDto, SupplierItemDto,
// PublicSupplierReviewDto (v4.1, TASK-285/287).

import type { DeliveryCoverage, DeliveryCoverageEntry } from "@/features/geo/types";

// Delivery-coverage shapes live in the shared geo feature (they match the backend
// DeliveryCoverageDto exactly). Re-exported so marketplace consumers have one import.
export type { DeliveryCoverage, DeliveryCoverageEntry };

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

/** Measured average delivery time to one destination region, produced by the nightly
 *  supplier-metrics worker job. Matches backend RegionDeliveryStatDto. */
export interface RegionDeliveryStat {
  regionCode: string;
  avgDeliveryDays: number;
  sampleSize: number;
}

export interface SupplierMetricsDto {
  rating: number | null;
  avgDeliveryDays: number | null;
  orderAccuracy: number | null;
  qualityScore: number | null;
  cancellationRate: number | null;
  responseTimeHours: number | null;
  updatedAt: string;
  // TASK-656 (T9): worker-computed delivery / response aggregates. All nullable — the
  // nightly job may not have run yet, or a metric may have no data behind it.
  deliveryByRegion?: RegionDeliveryStat[] | null;
  deliverySampleSize?: number | null;
  responseSampleSize?: number | null;
  aggregatesComputedAt?: string | null;
}

/** One day's point-in-time copy of a supplier's performance metrics, from
 *  `GET /api/marketplace/suppliers/{id}/metrics-history?days=90` (TASK-671).
 *  The array is ordered oldest → newest. Every metric is nullable — the nightly
 *  recompute job may have had no data behind it on that day. `orderAccuracy` and
 *  `cancellationRate` are 0..1 fractions (multiply by 100 for a percentage);
 *  `rating` / `qualityScore` are on a 0..5 scale. Matches backend
 *  `SupplierMetricsHistoryPointDto`. */
export interface SupplierMetricsHistoryPoint {
  /** "2026-06-03" */
  date: string;
  rating: number | null;
  avgDeliveryDays: number | null;
  orderAccuracy: number | null;
  qualityScore: number | null;
  cancellationRate: number | null;
  responseTimeHours: number | null;
  deliverySampleSize: number | null;
  responseSampleSize: number | null;
}

/** Supplier delivery coverage resolved against the calling buyer's region.
 *  Response of `GET /api/marketplace/suppliers/{id}/coverage` (TASK-651/657).
 *  Matches backend `SupplierCoverageForBuyerDto`. */
export type BuyerRegionStatus = "served" | "not_served" | "unknown";

export interface SupplierCoverageForBuyer {
  /** The supplier's full declared coverage (same shape as `SupplierProfileDto.deliveryCoverage`). */
  coverage: DeliveryCoverage;
  /** Region resolved for the buyer — from `?buyerRegionCode=` when passed, else the
   *  buyer's oldest active location's region. `null` when neither could be resolved. */
  buyerRegionCode: string | null;
  buyerRegionStatus: BuyerRegionStatus;
  /** The served-list entry matching the buyer's region (with its structured delivery
   *  fields) when `buyerRegionStatus === "served"`; `null` when the region is unknown
   *  or not in `served`. Matches backend `SupplierCoverageForBuyerDto.BuyerRegionEntry`. */
  buyerRegionEntry: DeliveryCoverageEntry | null;
  /** Worker-measured average delivery time to the buyer's region; `null` until the
   *  nightly job has a sample for that region. */
  measuredAvgDeliveryDaysToBuyerRegion: number | null;
  measuredSampleSize: number | null;
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
  // TASK-656 (T9): declared delivery coverage — NOT premium-gated, present for every caller.
  deliveryCoverage?: DeliveryCoverage | null;
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
  /** Structured Ukraine region code (TASK-655). Matches backend SupplierSearchDto.RegionCode. */
  regionCode?: string;
}

export interface CreateReviewRequest {
  rating: number;
  comment?: string;
}

export interface SupplierProfileUpdateRequest {
  region: string;
  /**
   * Single primary category, set at tenant creation and read-only afterward
   * (TASK-665/667). Still returned by GET (0 or 1 element) so the profile form can
   * display it; the update endpoint ignores it, so it is never sent in the payload.
   */
  categories?: string[];
  website?: string;
  /**
   * Structured delivery coverage (TASK-655) — replaces the legacy free-text
   * `deliveryRegions`. Patch semantics on the wire: omit / null leaves the stored
   * value untouched. Present on the GET response too (backend returns SupplierProfileDto).
   */
  deliveryCoverage?: DeliveryCoverage | null;
  workingHours?: string;
  paymentTerms?: string;
  isPublic: boolean;
  plan: SupplierPlan;
}

// ─── Filter state ─────────────────────────────────────────────────────────────

export interface MarketplaceFilters {
  /** Structured Ukraine region code (TASK-655); "" = no region filter. */
  regionCode: string;
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
  /** "physical" | "vchasno" | null (не обрано) — TASK-319/320. */
  signingMethod: string | null;
  /** Заповнено лише якщо signingMethod === "vchasno". */
  signingEmail: string | null;
  /** Адреса постачальника для фізичного підписання; заповнена лише поки статус awaiting_signature/active. */
  supplierLegalAddress: string | null;
}

export interface CreateCooperationRequestBody {
  message?: string;
  /** Юридична особа клієнта, від імені якої подається заявка (TASK-327/328, необовʼязково). */
  clientLegalEntityId?: string;
}

export type SigningMethod = "physical" | "vchasno";

export interface ChooseSigningMethodRequest {
  method: SigningMethod;
  email?: string;
}

export type MarketplaceOrderStatus =
  | "new"
  | "confirmed"
  | "shipped"
  | "delivered"
  | "cancelled";

/** One supplier-allocated batch on an order line (Phase 3, plan D4). Written by the
 *  supplier at ship time; readable by both parties (supplier via the table's own
 *  tenant_isolation, client via the inverted client_read policy). Ordered nearest-expiry
 *  first. Matches backend MarketplaceOrderItemBatchDto. */
export interface MarketplaceOrderItemBatchDto {
  id: string;
  /** "YYYY-MM-DD" */
  expiryDate: string;
  batchNumber: string | null;
  qty: number;
  /** Source supplier_stock row; null once that batch row has been purged. */
  supplierStockId: string | null;
}

/** Line item snapshot at order time. */
export interface MarketplaceOrderItemDto {
  id: string;
  supplierItemId: string | null;
  itemName: string;
  unit: string | null;
  price: number;
  qty: number;
  lineTotal: number;
  /** Supplier-allocated batches for this line (Phase 3, plan D4). Always present, possibly
   *  empty (legacy orders, or shipments made with the supplier's supplier_inventory module off). */
  batches: MarketplaceOrderItemBatchDto[];
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
  /** ISO date string, set when status transitions to "shipped". Null until then. */
  shippedAt: string | null;
  /** Supplier-entered whole days, set at the same time as shippedAt. Null until shipped. */
  estimatedDeliveryDays: number | null;
  /** ISO date string, set when status transitions to "delivered". Null until then. */
  deliveredAt: string | null;
  /** Supplier's free-text explanation for a delay (TASK-585). Null until set. */
  delayReason: string | null;
  /** Client-side user who placed the order. Null on orders placed before this column existed. */
  createdByUserId: string | null;
  /** Denormalized display name of {@link createdByUserId} at order-creation time. Null when unknown. */
  createdByUserName: string | null;
  /** Store the goods are headed to. Null on orders placed before TASK-586 — permanent/expected
   * there (historical orders can never be received through the new flow), not a loading state. */
  destinationStoreId: string | null;
  /** Supplier warehouse the order was picked from (Phase 3, plan D4). One source warehouse per
   * order. Null for legacy orders and for shipments made with the supplier's supplier_inventory
   * module off. */
  sourceWarehouseId: string | null;
  /** Supplier-set expected delivery date ("YYYY-MM-DD"). Filled at ship time from the request,
   * or derived as shippedAt + estimatedDeliveryDays. Null until shipped. */
  expectedDeliveryDate: string | null;
  items: MarketplaceOrderItemDto[];
}

/** How to resolve a barcode conflict on one order line (TASK-597). Omit/"auto" is only
 * safe when a pre-flight `checkOrderConflicts` call found no conflict for that line —
 * if a conflict exists and this is left "auto", the backend rejects the whole order (400). */
export type CatalogAction = "auto" | "link" | "create_new";

export interface CreateMarketplaceOrderItem {
  supplierItemId: string;
  qty: number;
  catalogAction?: CatalogAction;
  /** Required when catalogAction === "link" — id of the existing catalog Item to link to. */
  linkedItemId?: string;
}

export interface CreateMarketplaceOrderRequest {
  items: CreateMarketplaceOrderItem[];
  comment?: string;
  /** Required — the client's store this order is a future delivery to (ADR-033 Decision 2). */
  destinationStoreId: string;
}

// ─── Barcode conflict pre-flight check (TASK-597, order-time catalog provisioning) ────
// POST /api/marketplace/suppliers/{id}/orders/conflicts — same items shape as
// CreateMarketplaceOrderRequest.items, returns the (possibly empty) list of conflicts.

export interface BarcodeConflictExistingItem {
  id: string;
  name: string;
  imageUrl: string | null;
  barcodes: string[];
}

export interface BarcodeConflict {
  supplierItemId: string;
  existingItem: BarcodeConflictExistingItem;
}

// ─── Marketplace order receiving (TASK-586, ADR-033) ───────────────────────────
// Matches backend DTOs: ShelfGuard.Application/Features/Marketplace/Dtos/MarketplaceOrderReceiptDtos.cs
// (handoff .claude/logs/handoffs/586-to-frontend_backend-developer.md). Web scope is read-only
// (GET .../receipt) — the scan/count mutation endpoints are mobile-only for this stage.

export interface MarketplaceOrderReceiptItemDto {
  id: string;
  marketplaceOrderItemId: string;
  productId: string | null;
  /** What the employee was supposed to be scanning — always present, even before productId resolves. */
  itemNameSnapshot: string;
  productName: string | null;
  quantityOrdered: number;
  quantityReceived: number | null;
  /** "YYYY-MM-DD" */
  expiryDate: string | null;
  batchNumber: string | null;
  discrepancyNotes: string | null;
  isResolved: boolean;
  /** The supplier-shipped batch this sub-row was prefilled from (Phase 3, plan D4). Non-null
   *  means expiry/batch/ordered-qty arrived from the supplier's allocation. Null on legacy /
   *  module-off orders, where a line still produces exactly one blank receipt item. */
  sourceOrderItemBatchId: string | null;
}

export interface MarketplaceOrderReceiptDto {
  id: string;
  marketplaceOrderId: string;
  clientTenantId: string;
  supplierTenantId: string;
  destinationStoreId: string;
  /** "—" if somehow unresolved, shouldn't happen. */
  destinationStoreName: string;
  status: "draft" | "received";
  createdByUserId: string | null;
  receivedByUserId: string | null;
  receivedAt: string | null;
  createdAt: string;
  updatedAt: string;
  items: MarketplaceOrderReceiptItemDto[];
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
  /** MarketplaceOrder.orderNumber, коли тікет автоматично відкрито через розбіжність при
   * прийомці замовлення (TASK-599). Null для звичайного, вручну відкритого тікета. */
  orderNumber: string | null;
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
