export type ItemStatus = "safe" | "warning" | "critical" | "expired";

export interface DashboardStats {
  safe: number;
  warning: number;
  critical: number;
  expired: number;
}

export interface AttentionItem {
  id: string;
  name: string;
  sku: string;
  category: string;
  zone: string;
  quantity: number;
  reorderLevel: number;
  status: ItemStatus;
}

export interface StoreZone {
  id: string;
  name: string;
  type: string;
  status: ItemStatus;
  safe: number;
  warning: number;
  critical: number;
}
