import { apiClient } from '@/lib/api-client';
import type { DashboardStats, AiOrderListItem, RecentMovementsPage } from '../types';

interface MovementWire {
  id: string;
  movementType: string;
  productId: string;
  productName: string | null;
  fromStoreId: string | null;
  fromStoreName: string | null;
  toStoreId: string | null;
  toStoreName: string | null;
  quantity: number;
  quantityBefore: number | null;
  quantityAfter: number | null;
  unitPrice: number | null;
  totalAmount: number | null;
  referenceId: string | null;
  referenceType: string | null;
  notes: string | null;
  createdAt: string;
}

interface MovementPageWire {
  items: MovementWire[];
  total: number;
  page: number;
  pageSize: number;
}

export async function getStockSummary(locationId?: string): Promise<DashboardStats> {
  const { data } = await apiClient.get<DashboardStats>('/stock/summary', {
    params: { store_id: locationId },
  });
  return data;
}

export async function getAiOrders(): Promise<AiOrderListItem[]> {
  const { data } = await apiClient.get<AiOrderListItem[]>('/ai-orders');
  return data;
}

export async function getMovementProduct(productId: string): Promise<{ id: string; name: string }> {
  const { data } = await apiClient.get<{ id: string; name: string }>(`/items/${productId}`);
  return { id: data.id, name: data.name };
}

export async function getRecentMovements(limit = 5, locationId?: string): Promise<RecentMovementsPage> {
  const { data } = await apiClient.get<MovementPageWire>('/movements', {
    params: { page: 1, page_size: limit, store_id: locationId },
  });
  return {
    ...data,
    items: data.items.map((item) => ({
      ...item,
      fromLocationId: item.fromStoreId,
      fromLocationName: item.fromStoreName,
      toLocationId: item.toStoreId,
      toLocationName: item.toStoreName,
    })),
  };
}
