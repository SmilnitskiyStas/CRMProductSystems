import { apiClient } from '@/lib/api-client';
import type { Notification, PagedResult } from '../types';

export async function getNotificationHistory(): Promise<PagedResult<Notification>> {
  const { data } = await apiClient.get<PagedResult<Notification>>('/notifications/history');
  return data;
}
