// Audience Builder — Фаза 3 (TASK-430).
//
// Source of truth for this contract: `.claude/logs/tasks/429_2026-07-27_audience-builder-backend_backend-developer.md`
// ("API contract for frontend-developer" section) — NOT the earlier scratchpad design doc
// (`phase3-audience-builder-design.md`), which the backend agent deliberately deviated from in a
// few places (see that task log's "Deliberate deviations" section). All shapes below were cross-
// checked directly against `backend/ShelfGuard.Application/Features/MarketingAnalytics/
// AudienceBuilder/Dtos/AudienceBuilderDtos.cs` and `AudienceBuilderController.cs`. Wire casing:
// property NAMES are camelCase (ASP.NET Core default), enum VALUES are PascalCase strings
// (JsonStringEnumConverter, no naming-policy override — e.g. `"kind": "Text"`, not `"text"`).
//
// Self-contained on purpose (does not import from `features/marketing-analytics/types.ts` or
// `price-segments/types.ts`) — same "each feature owns its own types.ts" convention price-segments
// itself documents (see that feature's types.ts header comment). The two components explicitly
// named as reusable-as-is (SortableHeader/TablePaginationFooter from price-segments/components/
// TableControls.tsx) are imported directly in the table components, not duplicated here.

// ── Enums (serialize as PascalCase strings) ─────────────────────────────────────────────────────

export type AudienceTermKind = "Text" | "Category";
export type AudienceCombineMode = "Any" | "All";
export type CompetitorHorizon = "InPeriod" | "AllTime";

// ── Wire shape for one term chip ────────────────────────────────────────────────────────────────

/** Only `text` (Kind="Text") or `categoryId` (Kind="Category") is read server-side for the
 * matching kind — the other must be sent as null. A term missing its own kind's value is silently
 * dropped server-side (never a 400) — see AudienceBuilderDtos.cs's own remarks on AudienceTermRequest. */
export interface AudienceTermRequest {
  kind: AudienceTermKind;
  text: string | null;
  categoryId: string | null;
}

/**
 * Frontend-local term representation (Zustand store shape) — adds a stable client-only `id` (for
 * React keys / removal, never sent to the server) and, for Category terms, caches the
 * human-readable name + item count from the typeahead result so chips/labels don't need a second
 * lookup. Converted to the wire `AudienceTermRequest[]` shape via `toTermRequests()` in
 * `api/audienceBuilder.ts` right before a request is sent.
 */
export interface AudienceTerm {
  id: string;
  kind: AudienceTermKind;
  /** Set only when kind === "Text". */
  text: string | null;
  /** Set only when kind === "Category". */
  categoryId: string | null;
  categoryName: string | null;
  categoryItemCount: number | null;
}

// ── GET .../categories?search=&limit= ───────────────────────────────────────────────────────────

export interface AudienceCategoryOptionDto {
  categoryId: string;
  name: string;
  itemCount: number;
}

// ── Own-product audience: POST overview / buyers / matched-items ───────────────────────────────

/**
 * Shared filter shape for every "own product" read endpoint — one flat request type reused across
 * overview/buyers/matched-items (design doc §7, confirmed unchanged in the actual backend DTO).
 * `page`/`pageSize`/`sortBy`/`sortDescending` are ignored by the overview endpoint (a single
 * aggregate row, never paginated) — any values are accepted there. `canViewUnmaskedPii` is always
 * sent as `false` from this app: the controller re-resolves it server-side from the caller's own
 * role/capability for both `buyers`/`competitor/buyers` reads, never trusting a client-supplied
 * flag on those two endpoints (task log 429, §"PII masking — day 0").
 */
export interface AudienceBuildRequest {
  from: string; // yyyy-MM-dd
  to: string; // yyyy-MM-dd
  storeIds: string[] | null;
  terms: AudienceTermRequest[];
  mode: AudienceCombineMode;
  minQuantity: number | null;
  minAmount: number | null;
  excludedItemIds: string[] | null;
  page: number;
  pageSize: number;
  sortBy: string | null;
  sortDescending: boolean;
  canViewUnmaskedPii: boolean;
}

/** Own-filter fields only, no paging — export always fetches the FULL (capped-at-50k) result
 * server-side, never the caller's current on-screen page/sort (same shape as Фаза 2's
 * ExportPriceAudienceRequest). */
export interface ExportAudienceBuyersRequest {
  from: string;
  to: string;
  storeIds: string[] | null;
  terms: AudienceTermRequest[];
  mode: AudienceCombineMode;
  minQuantity: number | null;
  minAmount: number | null;
  excludedItemIds: string[] | null;
  unmaskPii: boolean;
}

export interface AudienceOverviewDto {
  participantsCount: number;
  itemsInSelectionCount: number;
  unitsPurchased: number;
  totalSpend: number;
  filtersHash: string;
  calculatedAt: string;
}

export interface AudienceBuyerRowDto {
  customerId: string;
  name: string;
  /** Already masked/unmasked by the server per the CALLER's own role/capability — render as-is,
   * never mask again client-side (unlike price-segments, this feature's read endpoints DO mask
   * on-screen phone server-side; only the export flow's `unmaskPii` toggle is a client decision). */
  phone: string | null;
  quantityPurchased: number;
  receiptCount: number;
  totalAmount: number;
}

export interface AudienceBuyerTableDto {
  totalCount: number;
  withPhoneCount: number;
  rows: AudienceBuyerRowDto[];
  page: number;
  pageSize: number;
  totalPages: number;
  sortBy: string;
  sortDescending: boolean;
  filtersHash: string;
  calculatedAt: string;
}

export type BuyersSortBy = "name" | "qty" | "receipts" | "amount";

/** Every SKU that matched the search, INCLUDING ones already excluded (`isExcluded: true`) and
 * ones with zero sales in the active period (all three sales fields 0 — a deliberate inclusion,
 * analysis doc §20.4, not a gap). `barcodesJoined` is null (never "") when the item has none. */
export interface MatchedItemRowDto {
  itemId: string;
  name: string;
  barcodesJoined: string | null;
  isExcluded: boolean;
  quantitySold: number;
  receiptCount: number;
  buyerCount: number;
}

export interface MatchedItemsTableDto {
  totalCount: number;
  rows: MatchedItemRowDto[];
  page: number;
  pageSize: number;
  totalPages: number;
  sortBy: string;
  sortDescending: boolean;
  filtersHash: string;
  calculatedAt: string;
}

export type MatchedItemsSortBy = "name" | "sold" | "receipts" | "buyers";

// ── Competitor audience: POST competitor/overview / competitor/buyers ──────────────────────────

/**
 * `ownTerms`/`ownExcludedItemIds` are the SAME term-builder state already used for the main
 * "Покупці товару" tab — the competitor tab only adds its own competitor-term chips on top. There
 * is deliberately no `ownMode`/min-quantity/min-amount here: the backend's `own_buyers_to_exclude`
 * set is a plain "bought ANY unit of ANY own-matching item, ever/in-period" existence check, never
 * gated by AND/OR mode or the quantity/amount thresholds (those only filter the MAIN audience's
 * customer list, task log 429).
 */
export interface CompetitorAudienceRequest {
  from: string;
  to: string;
  storeIds: string[] | null;
  ownTerms: AudienceTermRequest[];
  ownExcludedItemIds: string[] | null;
  competitorTerms: AudienceTermRequest[];
  horizon: CompetitorHorizon;
  page: number;
  pageSize: number;
  sortBy: string | null;
  sortDescending: boolean;
  canViewUnmaskedPii: boolean;
}

export interface ExportCompetitorBuyersRequest {
  from: string;
  to: string;
  storeIds: string[] | null;
  ownTerms: AudienceTermRequest[];
  ownExcludedItemIds: string[] | null;
  competitorTerms: AudienceTermRequest[];
  horizon: CompetitorHorizon;
  unmaskPii: boolean;
}

export interface CompetitorOverviewDto {
  newAudienceCount: number;
  competitorItemsCount: number;
  unitsPurchased: number;
  totalSpend: number;
  filtersHash: string;
  calculatedAt: string;
}

export interface CompetitorBuyerRowDto {
  customerId: string;
  name: string;
  phone: string | null;
  quantityPurchased: number;
  receiptCount: number;
  totalAmount: number;
}

export interface CompetitorBuyerTableDto {
  totalCount: number;
  withPhoneCount: number;
  rows: CompetitorBuyerRowDto[];
  page: number;
  pageSize: number;
  totalPages: number;
  sortBy: string;
  sortDescending: boolean;
  filtersHash: string;
  calculatedAt: string;
}

// ── Frontend-local filter bags — never sent to the wire as-is; `api/audienceBuilder.ts`'s builder
// functions map these (plus a paging bag) onto the actual request DTOs above. ───────────────────

export interface AudienceFilterState {
  from: string;
  to: string;
  storeIds: string[];
  terms: AudienceTerm[];
  mode: AudienceCombineMode;
  minQuantity: number | null;
  minAmount: number | null;
  excludedItemIds: string[];
}

export interface CompetitorFilterState {
  from: string;
  to: string;
  storeIds: string[];
  ownTerms: AudienceTerm[];
  ownExcludedItemIds: string[];
  competitorTerms: AudienceTerm[];
  horizon: CompetitorHorizon;
}

export interface TablePagingState {
  page: number;
  pageSize: number;
  sortBy: string | null;
  sortDescending: boolean;
}
