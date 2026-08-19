export interface Notification {
  id: string;
  eventType: string;
  channel: string;
  status: string;
  payload: string | null;
  createdAt: string;
  isRead: boolean;
  readAt: string | null;
  title: string | null;
  storeId: string | null;
  userId: string | null;
}

// Локальна копія — за конвенцією mobile (loyalty/customers/marketplace тримають
// свою копію в types.ts фічі, спільного mobile/lib модуля немає).
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export type NotificationCategory = 'expiry' | 'stock' | 'system';

export function categorize(eventType: string): NotificationCategory {
  if (eventType.startsWith('expiry')) return 'expiry';
  if (eventType.startsWith('stock') || eventType.startsWith('low_stock')) return 'stock';
  return 'system';
}
