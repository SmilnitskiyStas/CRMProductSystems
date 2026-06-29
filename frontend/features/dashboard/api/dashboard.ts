import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type { AttentionItem, DashboardStats, ItemStatus, StoreZone } from "../types";

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

async function getDashboardStats(): Promise<DashboardStats> {
  const summary = await api.get<StockSummaryDto>("/api/stock/summary");
  return {
    safe: summary.safe,
    warning: summary.warning,
    critical: summary.critical,
    expired: summary.expired,
  };
}

async function getAttentionItems(): Promise<AttentionItem[]> {
  const { items: batches } = await api.get<PagedResult<ProductStockDto>>("/api/stock?pageSize=200");
  return batches
    .filter((b) => b.status !== "safe")
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

async function getStoreZones(): Promise<StoreZone[]> {
  const zones = await api.get<ZoneSummaryDto[]>("/api/stock/zones-summary");
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

export const dashboardApi = {
  getStats: getDashboardStats,
  getAttentionItems,
  getStoreZones,
};
