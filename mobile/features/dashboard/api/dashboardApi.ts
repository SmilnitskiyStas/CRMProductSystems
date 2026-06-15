import { apiClient } from '@/lib/api-client';
import type { DashboardStats, AiOrderListItem, RecentMovementsPage } from '../types';

export async function getStockSummary(): Promise<DashboardStats> {
  const { data } = await apiClient.get<DashboardStats>('/stock/summary');
  return data;
}

export async function getAiOrders(): Promise<AiOrderListItem[]> {
  const { data } = await apiClient.get<AiOrderListItem[]>('/ai-orders');
  return data;
}

export async function getRecentMovements(limit = 5): Promise<RecentMovementsPage> {
  const { data } = await apiClient.get<RecentMovementsPage>('/movements', {
    params: { page: 1, page_size: limit },
  });
  return data;
}
