export interface ExpirySummaryStoreDto {
  storeId: string;
  storeName: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
}

export interface ExpirySummaryDto {
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  needsVerification: number;
  total: number;
  stores: ExpirySummaryStoreDto[];
}

export interface WriteOffByReasonDto {
  reason: string;
  count: number;
  totalLoss: number;
}

export interface WriteOffByDateDto {
  date: string;
  count: number;
  totalLoss: number;
}

export interface WriteOffAnalyticsDto {
  totalDocuments: number;
  totalLoss: number;
  byReason: WriteOffByReasonDto[];
  byDate: WriteOffByDateDto[];
}

export interface MovementByTypeDto {
  movementType: string;
  count: number;
  totalQuantity: number;
}

export interface MovementAnalyticsDto {
  totalMovements: number;
  totalQuantity: number;
  byType: MovementByTypeDto[];
}

export interface ZoneAnalyticsDto {
  zoneId: string;
  zoneName: string;
  zoneType: string;
  storeId: string;
  storeName: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  totalBatches: number;
}

export interface CategoryAnalyticsDto {
  categoryId: string | null;
  categoryName: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  totalBatches: number;
  totalQuantity: number;
}

export interface LossByStoreDto {
  storeId: string;
  storeName: string;
  totalLoss: number;
  writeOffCount: number;
}

export interface LossesDto {
  totalLoss: number;
  totalWriteOffs: number;
  averageLossPerWriteOff: number;
  byStore: LossByStoreDto[];
}

// ── Period comparison (ADR-016) ─────────────────────────────────────────────
// Opt-in via `?compare=true` on the existing endpoints below. When omitted
// (or false) the response shape is unchanged — see the flat DTOs above/below.

export interface WriteOffAnalyticsCompareDto {
  current: WriteOffAnalyticsDto;
  comparison: WriteOffAnalyticsDto;
  totalLossPercentChange: number | null;
}

export interface LossesCompareDto {
  current: LossesDto;
  comparison: LossesDto;
  totalLossPercentChange: number | null;
}

// ── POS Analytics ──────────────────────────────────────────────────────────────

export interface PosAnalyticsSummaryDto {
  totalRevenue: number;
  transactionCount: number;
  averageTicket: number;
  cashRevenue: number;
  cardRevenue: number;
  shiftCount: number;
  from: string;
  to: string;
}

export interface PosRevenueTrendPoint {
  date: string;
  revenue: number;
  transactions: number;
}

export interface PosRevenueTrendDto {
  points: PosRevenueTrendPoint[];
  groupBy: "day" | "week";
}

export interface PosTopProductItem {
  productId: string;
  productName: string;
  barcode: string;
  totalRevenue: number;
  totalQuantity: number;
  transactionCount: number;
}

export interface PosTopProductsDto {
  items: PosTopProductItem[];
}

export interface PosCashierStat {
  cashierId: string;
  cashierName: string;
  totalRevenue: number;
  transactionCount: number;
  averageTicket: number;
  shiftCount: number;
}

export interface PosCashierStatsDto {
  cashiers: PosCashierStat[];
}

// ── Category × product breakdown (interactive analytics + margin plan, TASK-483) ───────────
// MarginAmount/MarginPercent are null both when the caller can't see margin (ADR-027 —
// AnalyticsAuthorization.CanViewMargin resolved false server-side) and when the product itself
// has no Item.PricePurchase on file — the UI never needs to tell these two cases apart (both
// render as an absent/em-dash figure), it only needs to gate rendering of the columns entirely
// on canViewAnalyticsMargin (frontend/lib/roles.ts), never on whether these fields happen to be
// null for a given row.
//
// daysOfStockRemaining (TASK-491/494): TotalQuantity / ProductAdu.AduEffective, rounded to 1
// decimal by the server. NOT margin/cost data — never gated on canViewAnalyticsMargin, always
// rendered for the same audience as every other column here. null in two independent cases the
// UI does not need to (and cannot, from this field alone) tell apart: (a) the request wasn't
// store-scoped (no store_id — ADU is per-(product, store), a network-wide rollup has no single
// meaningful ADU to divide by), or (b) the product has no ProductAdu row yet, or its
// AduEffective is itself null/0 (a real "no usage history yet" state, not an error). Render "—"
// for null either way, same convention as marginAmount/marginPercent above.

export interface CategoryProductRowDto {
  productId: string;
  productName: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  totalQuantity: number;
  salesRevenue: number;
  unitsSold: number;
  marginAmount: number | null;
  marginPercent: number | null;
  daysOfStockRemaining: number | null;
}

export interface CategoryProductBreakdownDto {
  categoryId: string | null;
  categoryName: string;
  products: CategoryProductRowDto[];
}

// ── Losses × product breakdown (interactive analytics + margin plan, TASK-483) ─────────────
// Serves BOTH the losses-by-store and losses-by-reason drill-downs (independent optional
// AND-filters) — no margin fields at all on this DTO (ADR-027 §1: losses aren't margin-gated).

export interface LossByProductRowDto {
  productId: string;
  productName: string;
  quantity: number;
  lossAmount: number;
  sharePercent: number;
}

export interface LossesByProductDto {
  totalLoss: number;
  products: LossByProductRowDto[];
}

// ── POS Analytics — period comparison (ADR-016) ─────────────────────────────

export interface PosAnalyticsSummaryCompareDto {
  current: PosAnalyticsSummaryDto;
  comparison: PosAnalyticsSummaryDto;
  revenuePercentChange: number | null;
  transactionCountPercentChange: number | null;
}

/** Unlike PosRevenueTrendDto, compare mode returns two flat (sparse) point arrays, not a `points` wrapper. */
export interface PosRevenueTrendCompareDto {
  current: PosRevenueTrendPoint[];
  comparison: PosRevenueTrendPoint[];
  groupBy: "day" | "week";
  from: string;
  to: string;
  compareFrom: string;
  compareTo: string;
}

// ── Single-product sales trend (interactive analytics + margin plan, TASK-484) ─────────────
// Row-click drill-down from PosTopProductsTable, rendered inline via the extended
// ProductAnalyticsTab (frontend/features/inventory/components/ProductAnalyticsTab.tsx). No
// compare-mode variant — this is a snapshot trend off a table row, not a page-level KPI trend.
// marginAmount is null both when the caller can't see margin (ADR-027 — server-side
// AnalyticsAuthorization.CanViewMargin resolved false) and when the product itself has no
// Item.PricePurchase on file — same "don't distinguish, just gate on canViewAnalyticsMargin"
// rule as CategoryProductRowDto above.

export interface ProductSalesTrendPointDto {
  date: string;
  revenue: number;
  quantity: number;
  transactionCount: number;
  marginAmount: number | null;
}

export interface ProductSalesTrendDto {
  productId: string;
  productName: string;
  points: ProductSalesTrendPointDto[];
  groupBy: "day" | "week";
}

// ── Losses/write-offs trend over time (TASK-489/492) ────────────────────────
// Mirrors PosRevenueTrendDto's shape exactly (group_by day|week, points sorted ascending by
// date, sparse — not zero-filled for gap days). No margin fields and no compare-mode variant:
// LossAmount isn't margin-gated (ADR-027 §1, same reasoning as LossesByProductDto above), and
// TASK-489 didn't add a comparison endpoint for this one.

export interface LossesTrendPointDto {
  date: string;
  totalLoss: number;
  count: number;
}

export interface LossesTrendDto {
  points: LossesTrendPointDto[];
  groupBy: "day" | "week";
}

// ── Worst-performing products / dead stock (TASK-490/493) ──────────────────
// The dead-stock counterpart to PosTopProductItem/PosTopProductsDto -- but not simply that list
// sorted ascending. The backend query groups PosTransactionItems for top-products, so a
// zero-sale product never appears there at all (no rows to group). This DTO's source query
// instead starts from the catalog/stock side (active items currently on-hand) and LEFT-JOINs the
// sales rollup, so a zero-sale product still shows up with salesRevenue === 0 (never null).
// currentStock is what makes a zero-revenue row actionable -- it's the evidence of exactly how
// many units are sitting unsold. No margin fields, no barcode field (unlike PosTopProductItem).
// Products are already sorted ascending by salesRevenue (worst/zero first) by the server.

export interface WorstProductRowDto {
  productId: string;
  productName: string;
  salesRevenue: number;
  unitsSold: number;
  transactionCount: number;
  currentStock: number;
}

export interface WorstProductsDto {
  products: WorstProductRowDto[];
}

// ── Single-product ADU lookup (TASK-494: days-of-stock-remaining UI) ───────────────────────
// Mirrors backend AduDto (ShelfGuard.Application/Features/Adu/Dtos/AduDtos.cs) field-for-field.
// GET /api/adu/{storeId}/{productId} (frontend/features/analytics/api/adu.ts) 404s when no
// ProductAdu row has been calculated yet for this (store, product) pair -- callers should treat
// that the same as "no signal" (see useAdu.ts), never surface it as an error. aduEffective can
// independently be null OR a real 0 even on a 200 response (no usage history yet is a valid
// state, confirmed by TASK-491's own backend investigation) -- days-of-stock math must guard
// both, not just the 404 case.

export interface AduDto {
  storeId: string;
  productId: string;
  productName: string | null;
  adu30d: number | null;
  adu60d: number | null;
  adu90d: number | null;
  aduEffective: number | null;
  productGroup: number | null;
  validDays30d: number | null;
  validDays60d: number | null;
  calculatedAt: string;
}
