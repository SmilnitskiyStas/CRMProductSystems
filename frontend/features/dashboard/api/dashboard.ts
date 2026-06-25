import { api } from "@/lib/api";
import type { AttentionItem, DashboardStats, ItemStatus, StoreZone } from "../types";

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
  const batches = await api.get<ProductStockDto[]>("/api/stock");
  const stats: DashboardStats = { safe: 0, warning: 0, critical: 0, expired: 0 };
  for (const b of batches) {
    const s = b.status as keyof DashboardStats;
    if (s in stats) stats[s]++;
  }
  return stats;
}

async function getAttentionItems(): Promise<AttentionItem[]> {
  const batches = await api.get<ProductStockDto[]>("/api/stock");
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

// Static placeholder zones until /api/stores/:id/zones is implemented
async function getStoreZones(): Promise<StoreZone[]> {
  return [
    { id: "z1", name: "Молочні продукти", type: "refrigerated", status: "warning", safe: 12, warning: 4, critical: 1 },
    { id: "z2", name: "Овочі та фрукти", type: "fresh", status: "critical", safe: 8, warning: 2, critical: 3 },
    { id: "z3", name: "Бакалія", type: "dry", status: "safe", safe: 24, warning: 1, critical: 0 },
    { id: "z4", name: "М'ясний відділ", type: "refrigerated", status: "critical", safe: 6, warning: 3, critical: 4 },
    { id: "z5", name: "Заморожені", type: "frozen", status: "warning", safe: 15, warning: 3, critical: 0 },
    { id: "z6", name: "Напої", type: "dry", status: "safe", safe: 18, warning: 0, critical: 0 },
  ];
}

export const dashboardApi = {
  getStats: getDashboardStats,
  getAttentionItems,
  getStoreZones,
};
