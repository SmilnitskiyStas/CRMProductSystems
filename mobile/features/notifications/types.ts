export interface Notification {
  id: string;
  eventType: string;
  channel: string;
  status: string;
  payload: string | null;
  createdAt: string;
}

export type NotificationCategory = 'expiry' | 'stock' | 'system';

export function categorize(eventType: string): NotificationCategory {
  if (eventType.startsWith('expiry')) return 'expiry';
  if (eventType.startsWith('stock') || eventType.startsWith('low_stock')) return 'stock';
  return 'system';
}
