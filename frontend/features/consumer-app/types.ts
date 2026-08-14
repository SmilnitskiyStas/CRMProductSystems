// Consumer App feature (TASK-500) — a tenant-admin-only settings area for the consumer-facing
// mobile app. Starts with just the bonus/loyalty program settings; the page this feature backs
// (`app/(dashboard)/consumer-app/page.tsx`) is deliberately structured to grow additional cards
// (news, promos, etc.) later without a route change — see that page's own comment. Only the
// loyalty section is implemented today; no other sections are scaffolded.

/** Exactly "qr" or "barcode" — how a tenant's consumers render their universal bonus-card code. */
export type CustomerCodeFormat = "qr" | "barcode";

/** GET /api/settings/loyalty response (LoyaltySettingsController, enterprise_admin+ only). */
export interface LoyaltyProgramSettings {
  isEnabled: boolean;
  accrualRatePercent: number;
  redemptionCapPercent: number;
  minRedemptionBalance: number;
  codeTtlSeconds: number;
  /** TASK-499/500: "qr" or "barcode". Defaults to "barcode" server-side when never saved. */
  customerCodeFormat: CustomerCodeFormat;
  updatedAt: string | null;
}

/**
 * PUT /api/settings/loyalty request body. Full replace — no partial-update semantics, every
 * field must always be sent (mirrors UpsertLoyaltyProgramSettingsRequest on the backend).
 */
export interface UpdateLoyaltyProgramSettingsRequest {
  isEnabled: boolean;
  accrualRatePercent: number;
  redemptionCapPercent: number;
  minRedemptionBalance: number;
  codeTtlSeconds: number;
  customerCodeFormat: CustomerCodeFormat;
}

// ── Banners (TASK-522, second section of the Consumer App page) ────────────
// BannersController (enterprise_admin+, TASK-521) — promotional banners shown on the
// consumer app's home feed. Body/Terms are the entity's raw "\n"-joined text at this admin
// layer (a plain <textarea> round-trips them directly) — only the separate consumer-facing
// endpoint (ConsumerContentController, mobile-only) splits them into string[].

export type BannerDetailMode = "internal" | "external";

/** GET /api/banners, GET /api/banners/{id} response shape. */
export interface BannerDto {
  id: string;
  title: string;
  eyebrow: string | null;
  description: string;
  body: string;
  terms: string;
  imageUrl: string | null;
  icon: string;
  backgroundColor: string;
  accentColor: string;
  detailMode: BannerDetailMode;
  externalUrl: string | null;
  validFrom: string;
  validUntil: string | null;
  isActive: boolean;
  isCurrentlyActive: boolean;
  sortOrder: number;
  locationIds: string[];
  productIds: string[];
  viewCount: number;
  clickCount: number;
  createdAt: string;
  updatedAt: string | null;
}

/** POST /api/banners request body. */
export interface CreateBannerRequest {
  title: string;
  description: string;
  body: string;
  terms: string;
  icon: string;
  backgroundColor: string;
  accentColor: string;
  detailMode: BannerDetailMode;
  eyebrow?: string | null;
  imageUrl?: string | null;
  externalUrl?: string | null;
  validFrom?: string | null;
  validUntil?: string | null;
  sortOrder?: number;
  locationIds?: string[];
  productIds?: string[];
}

/**
 * PUT /api/banners/{id} request body — full replace, every field re-sent every time
 * (no partial-update semantics; mirrors UpdateLoyaltyProgramSettingsRequest's convention
 * above). Unlike Create, `validFrom` is required — `Banner.Update` has no "keep previous"
 * fallback the way `Banner.Create` defaults it to `DateTime.UtcNow` when omitted.
 */
export interface UpdateBannerRequest {
  title: string;
  description: string;
  body: string;
  terms: string;
  icon: string;
  backgroundColor: string;
  accentColor: string;
  detailMode: BannerDetailMode;
  validFrom: string;
  eyebrow?: string | null;
  imageUrl?: string | null;
  externalUrl?: string | null;
  validUntil?: string | null;
  sortOrder?: number;
  locationIds?: string[];
  productIds?: string[];
}

/** GET /api/banners/{id}/analytics response. */
export interface BannerAnalyticsDto {
  viewCount: number;
  clickCount: number;
  ctr: number;
}

// ── Promo products (third section) ──────────────────────────────────────────
// Reuses the pre-existing DiscountsController as-is (no new backend, TASK-521 plan) —
// this feature owns the frontend types/client for it since no other feature calls
// /api/discounts yet.

/** expiry | overstock | promo */
export type DiscountReason = "expiry" | "overstock" | "promo";
/** pending | active | expired | cancelled */
export type DiscountStatus = "pending" | "active" | "expired" | "cancelled";

/** GET /api/discounts, GET /api/discounts/{id} response shape (DiscountsController). */
export interface DiscountDto {
  id: string;
  tenantId: string;
  productId: string;
  storeId: string;
  productStockId: string | null;
  discountPercent: number;
  priceOriginal: number | null;
  priceDiscounted: number | null;
  reason: DiscountReason;
  validFrom: string;
  validUntil: string | null;
  status: DiscountStatus;
  autoApplied: boolean;
  createdBy: string | null;
  approvedBy: string | null;
  createdAt: string;
  approvedAt: string | null;
  webhookSentAt: string | null;
}

/** POST /api/discounts request body. This admin surface always sends reason="promo". */
export interface CreateDiscountRequest {
  productId: string;
  storeId: string;
  discountPercent: number;
  reason?: DiscountReason;
  productStockId?: string | null;
  priceOriginal?: number | null;
  validFrom?: string | null;
  validUntil?: string | null;
}
