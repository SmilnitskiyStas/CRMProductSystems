import { apiClient } from '@/lib/api-client';
import type { Notification } from '../types';

export async function getNotificationHistory(): Promise<Notification[]> {
  const { data } = await apiClient.get<Notification[]>('/notifications/history');
  return data;
}
