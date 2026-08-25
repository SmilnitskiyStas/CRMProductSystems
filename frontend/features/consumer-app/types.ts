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

// ── Loyalty tier ladder (TASK-615 backend, TASK-620 this admin UI) ─────────
// GET/PUT /api/settings/loyalty/tiers (LoyaltyTierSettingsController, same
// AtLeastEnterpriseAdmin tier as LoyaltySettingsController above). Mirrors
// LoyaltyDtos.cs's LoyaltyTierDefinitionDto / UpsertTierRequest records field-for-field.

/**
 * GET /api/settings/loyalty/tiers response item — one rung of the tenant's ladder, ordered
 * by `sortOrder` ascending (lowest tier first). Empty array (never null) when the tenant has
 * no ladder configured yet.
 */
export interface LoyaltyTierDefinitionDto {
  id: string;
  name: string;
  sortOrder: number;
  minCompositeScore: number;
  accrualMultiplier: number;
  discountPercent: number;
}

/**
 * One rung in the PUT /api/settings/loyalty/tiers request body — a full bulk-replace of the
 * whole ladder, matched server-side against existing rows by `sortOrder` (the ladder's natural
 * unique key, see LoyaltyService.UpsertTierLadderAsync's remarks). No `id` — a row whose
 * `sortOrder` matches an existing tier's keeps that tier's database Id; a `sortOrder` that
 * matches nothing existing creates a new tier; an existing `sortOrder` missing from this array
 * gets deleted. TierLadderSection.tsx always renumbers `sortOrder` to the row's 0-based position
 * in the on-screen list before submitting, and warns the admin first (via ConfirmDialog) when
 * that renumbering would change an already-saved row's `sortOrder` — see that component's
 * `reordered` check for why a drag (or an add/remove above an existing row) can silently
 * reassign which tier's data a given database row now represents.
 */
export interface UpsertTierRequest {
  name: string;
  sortOrder: number;
  minCompositeScore: number;
  accrualMultiplier: number;
  discountPercent: number;
}

// ── Banners (TASK-522, second section of the Consumer App page) ────────────
// BannersController (enterprise_admin+, TASK-521) — promotional banners shown on the
// consumer app's home feed. Body/Terms are the entity's raw "\n"-joined text at this admin
// layer (a plain <textarea> round-trips them directly) — only the separate consumer-facing
// endpoint (ConsumerContentController, mobile-only) splits them into string[].

export type BannerDetailMode = "internal" | "external";

/**
 * TASK-525: "draft" | "running" | "past" — computed server-side (BannerService.LifecycleStatusOf),
 * never stored directly. draft = never published (publishedAt is null); running = published AND
 * isCurrentlyActive; past = published but not currently active (window elapsed, or manually
 * paused via IsActive=false).
 */
export type BannerLifecycleStatus = "draft" | "running" | "past";

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
  publishedAt: string | null;
  lifecycleStatus: BannerLifecycleStatus;
}

/**
 * POST /api/banners request body. `publishImmediately` (TASK-524) defaults to `true`
 * server-side, but every caller here always sends it explicitly (BannerForm's toggle) rather
 * than relying on the server default — keeps the client's intent unambiguous in the request.
 */
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
  publishImmediately: boolean;
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

// ── Theme (TASK-536/537, Retailer Admin "Design" section) ──────────────────
// GET/PUT /api/v1/mobile/theme (MobileThemeController, enterprise_admin+) — the tenant's single
// MobileTheme row, composed LIVE into GET /api/v1/mobile/config's `theme` on every anonymous
// consumer read (TASK-534/MobileThemeService remarks). LIVE-EFFECT CAVEAT: unlike the rest of
// this platform's Draft→Preview→Publish model, there is no draft/publish protection for theme
// yet (pending TASK-544) — a successful PUT here takes effect in production immediately, for
// every consumer currently viewing the app. See ThemeEditorSection's inline notice.

/** Exactly "compact" or "comfortable" — matches the mobile client's own RetailThemeConfig.spacing
 *  union (`mobile/features/mobile-config/types.ts`) and its AJV validation enum exactly. */
export type ThemeSpacingPreset = "compact" | "comfortable";

/** GET/PUT /api/v1/mobile/theme response shape (MobileThemeDto). `updatedAt` is null when the
 *  tenant never saved a theme yet — the response then reflects MobileTheme.CreateDefault's
 *  built-in defaults without a row existing (same "propose defaults, don't fabricate a save"
 *  convention as LoyaltyProgramSettings above). */
export interface MobileThemeDto {
  logoUrl: string | null;
  primaryColor: string;
  secondaryColor: string;
  backgroundColor: string;
  surfaceColor: string;
  textPrimaryColor: string;
  textSecondaryColor: string;
  buttonRadius: number;
  cardRadius: number;
  spacingPreset: ThemeSpacingPreset;
  updatedAt: string | null;
}

/**
 * PUT /api/v1/mobile/theme request body — full replace, every field always sent (mirrors
 * UpdateLoyaltyProgramSettingsRequest's convention above; no partial-update semantics).
 */
export interface UpdateMobileThemeRequest {
  logoUrl: string | null;
  primaryColor: string;
  secondaryColor: string;
  backgroundColor: string;
  surfaceColor: string;
  textPrimaryColor: string;
  textSecondaryColor: string;
  buttonRadius: number;
  cardRadius: number;
  spacingPreset: ThemeSpacingPreset;
}

/**
 * One rejected field from MobileThemeController's 400 response
 * (`{ errors: MobileConfigValidationError[] }`) — a different shape than this codebase's usual
 * flat `{ error: string }` convention (see `lib/api.ts`'s `ApiError.body`).
 */
export interface MobileThemeValidationError {
  field: string;
  message: string;
}

/**
 * Client-side mirror of `MobileThemeWhitelists.cs` — kept in sync manually since that class is
 * deliberately dependency-free C# with no generated TS output. Reused by ThemeEditorSection's
 * zod schema so the client never round-trips an obviously-invalid value the server will reject.
 */
export const THEME_BUTTON_RADIUS_MIN = 0;
export const THEME_BUTTON_RADIUS_MAX = 32;
export const THEME_CARD_RADIUS_MIN = 0;
export const THEME_CARD_RADIUS_MAX = 40;
export const THEME_LOGO_URL_MAX_LENGTH = 2048;
export const THEME_HEX_COLOR_PATTERN = /^#[0-9A-Fa-f]{6}$/;
export const THEME_SPACING_PRESETS = ["compact", "comfortable"] as const;

// ── App Builder (TASK-539, Retailer Admin "Pages" section) ─────────────────
// GET /api/v1/mobile/blocks[/{type}] (MobileBlocksController, TASK-538) and
// GET/PUT /api/v1/mobile/config/draft (MobileConfigDraftController, TASK-538b). The draft's
// `ConfigurationJson` is a whole-document string (validated by the backend's
// MobileConfigValidator/MobileConfigWhitelists, mirrored here manually the same way THEME_* mirrors
// MobileThemeWhitelists.cs above — those C# classes are dependency-free and emit no generated TS).

/** `pages.*.blocks[].type` — backend/.../MobileConfig/MobileConfigWhitelists.cs `BlockTypes`. */
export const MOBILE_CONFIG_BLOCK_TYPES = [
  "heroBanner", "bannerCarousel",
  "loyaltyCard", "loyaltyBalance",
  "promotionCarousel", "promotionGrid",
  "productCarousel", "productGrid",
  "sectionHeader", "quickActions",
  "newsList", "storeList",
] as const;
export type MobileConfigBlockType = (typeof MOBILE_CONFIG_BLOCK_TYPES)[number];

/** `features` object keys — MobileConfigWhitelists.FeatureKeys. Values are always boolean. */
export const MOBILE_CONFIG_FEATURE_KEYS = [
  "loyalty", "promotions", "catalog", "coupons", "news", "receipts", "delivery", "personalOffers",
] as const;
export type MobileConfigFeatureKey = (typeof MOBILE_CONFIG_FEATURE_KEYS)[number];
export type MobileConfigFeatures = Record<MobileConfigFeatureKey, boolean>;

/** `navigation[].type` — MobileConfigWhitelists.NavigationTypes. */
export const MOBILE_CONFIG_NAVIGATION_TYPES = [
  "home", "promotions", "catalog", "loyalty", "coupons", "stores", "news", "profile",
] as const;
export type MobileConfigNavigationType = (typeof MOBILE_CONFIG_NAVIGATION_TYPES)[number];

/** MASTER SPEC §8 — navigation must have between 2 and 5 items. */
export const MOBILE_CONFIG_NAVIGATION_MIN_ITEMS = 2;
export const MOBILE_CONFIG_NAVIGATION_MAX_ITEMS = 5;

/**
 * `navigation[].icon` — MobileConfigWhitelists.NavigationIcons (TASK-542). Deliberately matches
 * the already-shipped mobile-side icon set exactly (`mobile/features/mobile-config/validation.ts`'s
 * AJV `icon` enum) — semantic keys the mobile renderer's icon registry maps to its own native
 * icon components, not lucide-react names. `NavigationBuilderSection.tsx` maps these same keys to
 * lucide-react components separately, for admin-side preview only.
 */
export const MOBILE_CONFIG_NAVIGATION_ICONS = [
  "home", "tag", "grid", "qr", "ticket", "map", "news", "user",
] as const;
export type MobileConfigNavigationIcon = (typeof MOBILE_CONFIG_NAVIGATION_ICONS)[number];

/**
 * Client-side-only guard for `navigation[].label` (TASK-542 Navigation Builder). NOTE: unlike
 * every other `MOBILE_CONFIG_*` constant above, this one does NOT mirror a backend whitelist —
 * `MobileConfigValidator.ValidateNavigation` only requires `label` to be a non-missing string
 * (MASTER SPEC §8: "label stays a free string"), with no length cap today. This cap exists purely
 * so the admin UI can't produce a label that's empty or absurdly long; the server will happily
 * accept a shorter/longer value if this constant ever drifts from actual server behavior.
 */
export const MOBILE_CONFIG_NAVIGATION_LABEL_MAX_LENGTH = 30;

export interface MobileConfigNavigationItem {
  type: MobileConfigNavigationType;
  label: string;
  icon: string;
}

/**
 * `pages` object keys — MobileConfigWhitelists.PageNames. TASK-539 first edited only `home`;
 * TASK-541 generalized the App Builder canvas (`AppBuilderCanvas.tsx`'s `withPageBlocks`) to all
 * four via a page-tab selector. Profile/Auth/Security are deliberately absent from this whitelist
 * — they stay system-controlled and are never retailer-configurable.
 */
export const MOBILE_CONFIG_PAGE_NAMES = ["home", "promotions", "catalog", "news"] as const;
export type MobileConfigPageName = (typeof MOBILE_CONFIG_PAGE_NAMES)[number];

/** Only schemaVersion MobileConfigWhitelists.SupportedSchemaVersions accepts today. */
export const MOBILE_CONFIG_CURRENT_SCHEMA_VERSION = 1;

/** One block instance placed on a page — `pages.*.blocks[]` shape (MobileConfigValidator's `BlockKeys`). */
export interface MobileConfigBlockInstance {
  id: string;
  type: MobileConfigBlockType;
  order: number;
  props: Record<string, unknown>;
}

export interface MobileConfigPage {
  blocks: MobileConfigBlockInstance[];
}

/**
 * The whole `MobileConfigurationVersion.ConfigurationJson` document (parsed). Kept permissive
 * (`Partial`) on `features`/`pages` because a document already saved by another surface may not
 * populate every whitelisted key — the *seed* this feature writes for a brand-new tenant (see
 * `AppBuilderCanvas.tsx`'s `buildSeedDocument`) always populates every `features` key, a minimal
 * valid `navigation`, and (TASK-541) an empty-`blocks` entry for every whitelisted `pages` key,
 * but this type must still describe any document the draft endpoint could hand back — including
 * one saved before TASK-541 that only ever populated `pages.home`.
 */
export interface MobileConfigDocument {
  schemaVersion: number;
  features: Partial<MobileConfigFeatures>;
  navigation: MobileConfigNavigationItem[];
  pages: Partial<Record<MobileConfigPageName, MobileConfigPage>>;
}

// ── Block Registry (TASK-538) ───────────────────────────────────────────────
// GET /api/v1/mobile/blocks[/{type}] (MobileBlocksController, AtLeastEnterpriseAdmin). Not
// tenant-scoped — the same fixed catalog for every tenant.

/** `BlockPropDefinition.Type` — backend BlockPropTypes constants. Kept as `string`, not a union,
 *  because the registry response is the source of truth and a new value should render gracefully
 *  (falls back to a plain text input in the Property Editor, TASK-540) instead of a client build
 *  break. */
export type BlockPropType = "string" | "int" | "bool" | "enum" | "url" | "stringArray" | "productIds";

/** One entry in a block type's `validationSchema` (TASK-540's Property Editor generates a field
 *  per entry; this task only consumes `defaultProps`, not this schema, when placing a block). */
export interface BlockPropDefinitionDto {
  name: string;
  type: BlockPropType;
  required: boolean;
  default: unknown;
  minLength: number | null;
  maxLength: number | null;
  min: number | null;
  max: number | null;
  allowedValues: string[] | null;
  minItems: number | null;
  maxItems: number | null;
}

/** GET /api/v1/mobile/blocks response item / GET /api/v1/mobile/blocks/{type} response. */
export interface BlockDefinitionDto {
  type: MobileConfigBlockType;
  displayName: string;
  icon: string;
  category: string;
  defaultProps: Record<string, unknown>;
  validationSchema: BlockPropDefinitionDto[];
  supportedDataSource: string;
}

// ── Draft CRUD (TASK-538b) ──────────────────────────────────────────────────
// GET/PUT /api/v1/mobile/config/draft (MobileConfigDraftController, AtLeastEnterpriseAdmin).

/** PUT request body — whole-document replace, same read-modify-write caveat as the theme editor's
 *  full-replace convention, except here the "document" is the entire config, not one row's fields. */
export interface SaveMobileConfigDraftRequest {
  configurationJson: string;
}

/**
 * GET/PUT response shape (`MobileConfigDraftResponse`). `hasDraft: false` is the "tenant never
 * saved a draft yet" case (still `200`, never `404`) — `configurationJson` is `null` then, and the
 * App Builder canvas seeds its own minimal valid starting document instead of trusting one from
 * the server (see `buildSeedDocument` in `AppBuilderCanvas.tsx`).
 */
export interface MobileConfigDraftResponse {
  hasDraft: boolean;
  mobileConfigurationId: string | null;
  versionId: string | null;
  version: number;
  schemaVersion: number;
  status: string;
  configurationJson: string | null;
  createdBy: string | null;
  createdAt: string | null;
}

/**
 * One rejected field from `MobileConfigDraftController`'s 400 response
 * (`{ errors: MobileConfigValidationError[] }`) — same structured shape `MobileThemeController`
 * established (`MobileThemeValidationError` above), reused verbatim here.
 */
export interface MobileConfigDraftValidationError {
  field: string;
  message: string;
}

// ── Version History + Rollback + Publish (TASK-545/546) ────────────────────────────────────
// GET /api/v1/mobile/config/versions, POST /api/v1/mobile/config/versions/{id}/rollback
// (MobileConfigVersionsController), POST /api/v1/mobile/config/publish (MobileConfigPublishController).
// The 400 validation-failure shape for both POSTs is byte-identical to the draft endpoint's
// (`{ errors: [{ field, message }] }`) — callers reuse `extractDraftValidationErrors` from
// `../api/mobileConfigDraft` rather than a duplicate parser.

/** `MobileConfigurationVersionStatus.cs` constants — lowercase, never any other casing. */
export type MobileConfigVersionStatus = "draft" | "published" | "archived";

/**
 * One row of `GET /api/v1/mobile/config/versions` (`MobileConfigVersionSummaryResponse`),
 * newest-first by `version` descending. Every tenant has at most one `"published"` row and
 * exactly one `"draft"` row at a time (the row currently open in AppBuilderCanvas/ThemeEditorSection/
 * NavigationBuilderSection) — every other row is `"archived"`. Rollback is only a meaningful action
 * against an `"archived"` row: the backend rejects a rollback target equal to either the current
 * published or draft version (`CannotRollbackToCurrentVersion`), so the UI never renders a rollback
 * action for those two statuses either.
 */
export interface MobileConfigVersionSummary {
  id: string;
  version: number;
  status: MobileConfigVersionStatus;
  createdAt: string;
  publishedAt: string | null;
  createdBy: string | null;
}

/**
 * `POST /api/v1/mobile/config/publish` and `POST .../versions/{id}/rollback` response shape
 * (`MobileConfigPublishedResponse`) — the version that just became the tenant's new published
 * version. Neither caller in this feature reads `configurationJson` today (both just refetch the
 * version list + draft/theme after success), but it's included here for shape completeness.
 */
export interface MobileConfigPublishedResult {
  mobileConfigurationId: string;
  versionId: string;
  version: number;
  schemaVersion: number;
  configurationJson: string;
  createdBy: string | null;
  createdAt: string;
  publishedAt: string;
}
