import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type {
  AttentionItem,
  DashboardStats,
  ExpirySummaryCompareDto,
  ItemStatus,
  StoreZone,
  WeeklyKpiDto,
} from "../types";

interface StockSummaryDto {
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  needsVerification: number;
  total: number;
}

interface ProductStockDto {
  id: string;
  productId: string;
  productName: string;
  productBarcode: string | null;
  storeId: string;
  storeName: string;
  zoneId: string | null;
  zoneName: string | null;
  shelfNumber: number | null;
  batchNumber: string | null;
  quantity: number;
  quantityInitial: number;
  expiryDate: string;
  daysLeft: number;
  status: string;
  sourceType: string | null;
  addedAt: string;
  lastCheckedAt: string;
}

/** Appends one repeated `storeIds=<id>` query param per array entry. Empty array = all stores
 * (nothing appended), matching today's `store_id == null` semantics on the backend. */
function withStores(path: string, storeIds: string[]): string {
  if (storeIds.length === 0) return path;
  const sep = path.includes("?") ? "&" : "?";
  const qs = storeIds.map((id) => `storeIds=${encodeURIComponent(id)}`).join("&");
  return `${path}${sep}${qs}`;
}

async function getDashboardStats(storeIds: string[]): Promise<DashboardStats> {
  const summary = await api.get<StockSummaryDto>(withStores("/api/stock/summary", storeIds));
  return {
    safe: summary.safe,
    warning: summary.warning,
    critical: summary.critical,
    expired: summary.expired,
  };
}

async function getAttentionItems(storeIds: string[]): Promise<AttentionItem[]> {
  const { items: batches } = await api.get<PagedResult<ProductStockDto>>(
    withStores("/api/stock?pageSize=200", storeIds),
  );
  return batches
    // sold_out batches have nothing left to act on (no qty to reorder-check/expiry-track) —
    // exclude them so they don't take up space in the "needs attention" list.
    .filter((b) => b.status !== "safe" && b.status !== "sold_out")
    .map((b) => ({
      id: b.id,
      productId: b.productId,
      name: b.productName,
      sku: b.batchNumber ?? b.productBarcode ?? "—",
      category: b.zoneName ?? "—",
      zone: b.zoneName ?? "—",
      quantity: b.quantity,
      reorderLevel: 0,
      status: b.status as ItemStatus,
    }))
    .sort((a, b) => {
      const order: ItemStatus[] = ["expired", "critical", "warning", "safe"];
      return order.indexOf(a.status) - order.indexOf(b.status);
    });
}

interface ZoneSummaryDto {
  zoneId: string;
  name: string;
  type: string;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
}

async function getStoreZones(storeIds: string[]): Promise<StoreZone[]> {
  const zones = await api.get<ZoneSummaryDto[]>(withStores("/api/stock/zones-summary", storeIds));
  return zones.map((z) => ({
    id: z.zoneId,
    name: z.name,
    type: z.type,
    safe: z.safe,
    warning: z.warning,
    critical: z.critical,
    status: (z.critical > 0 ? "critical" : z.warning > 0 ? "warning" : "safe") as ItemStatus,
  }));
}

// ── Period comparison (ADR-016) ─────────────────────────────────────────────

async function getExpirySummaryCompare(
  storeIds: string[],
  compareWeeksAgo = 1,
): Promise<ExpirySummaryCompareDto> {
  const qs = new URLSearchParams();
  for (const id of storeIds) qs.append("storeIds", id);
  if (compareWeeksAgo !== 1) qs.set("compareWeeksAgo", String(compareWeeksAgo));
  const q = qs.toString();
  return api.get<ExpirySummaryCompareDto>(`/api/analytics/expiry-summary/compare${q ? `?${q}` : ""}`);
}

async function getWeeklyKpi(storeIds: string[]): Promise<WeeklyKpiDto> {
  return api.get<WeeklyKpiDto>(withStores("/api/analytics/dashboard/weekly-kpi", storeIds));
}

export const dashboardApi = {
  getStats: getDashboardStats,
  getAttentionItems,
  getStoreZones,
  getExpirySummaryCompare,
  getWeeklyKpi,
};
