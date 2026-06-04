import { getToken } from "@/lib/api";
import type { Product } from "@/features/inventory/types";
import type { AttentionItem, DashboardStats, ItemStatus, StoreZone } from "../types";

const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const token = getToken();
  const res = await fetch(`${API_BASE}${path}`, {
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init?.headers,
    },
    ...init,
  });
  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new Error(body || `HTTP ${res.status}`);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

function deriveStatus(product: Product): ItemStatus {
  if (product.stockQuantity === 0) return "expired"; // placeholder until expiry tracking endpoint
  if (product.stockQuantity <= product.reorderLevel * 0.5) return "critical";
  if (product.stockQuantity <= product.reorderLevel) return "warning";
  return "safe";
}

// Derives dashboard stats from products until /api/analytics/dashboard is implemented
async function getDashboardStats(): Promise<DashboardStats> {
  const products = await apiFetch<Product[]>("/api/products");
  const stats: DashboardStats = { safe: 0, warning: 0, critical: 0, expired: 0 };
  for (const p of products) {
    stats[deriveStatus(p)]++;
  }
  return stats;
}

async function getAttentionItems(): Promise<AttentionItem[]> {
  const products = await apiFetch<Product[]>("/api/products");
  return products
    .filter((p) => deriveStatus(p) !== "safe")
    .map((p) => ({
      id: p.id,
      name: p.name,
      sku: p.sku,
      category: p.category,
      zone: "—", // zone data available once /api/stock is implemented
      quantity: p.stockQuantity,
      reorderLevel: p.reorderLevel,
      status: deriveStatus(p),
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
