import { apiClient } from '@/lib/api-client';
import type { DashboardStats } from '../types';

export async function getDashboardStats(): Promise<DashboardStats> {
  const { data } = await apiClient.get<DashboardStats>('/analytics/expiry-summary');
  return data;
}
