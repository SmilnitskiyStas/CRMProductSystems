import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type { NotificationSetting, NotificationHistoryItem, NotificationHistoryFilters } from "../types";

export async function fetchNotificationSettings(): Promise<NotificationSetting[]> {
  return api.get<NotificationSetting[]>("/api/notifications/settings");
}

export async function updateNotificationSetting(
  eventType: string,
  channel: string,
  isEnabled: boolean,
): Promise<void> {
  await api.put("/api/notifications/settings", { eventType, channel, isEnabled });
}

export async function fetchNotificationHistory(
  filters: NotificationHistoryFilters = {},
): Promise<PagedResult<NotificationHistoryItem>> {
  const qs = new URLSearchParams();
  if (filters.search) qs.set("search", filters.search);
  if (filters.eventType) qs.set("eventType", filters.eventType);
  if (filters.userId) qs.set("userId", filters.userId);
  if (filters.storeId) qs.set("storeId", filters.storeId);
  if (filters.dateFrom) qs.set("dateFrom", filters.dateFrom);
  if (filters.dateTo) qs.set("dateTo", filters.dateTo);
  if (filters.page) qs.set("page", String(filters.page));
  if (filters.pageSize) qs.set("pageSize", String(filters.pageSize));
  const q = qs.toString();
  return api.get<PagedResult<NotificationHistoryItem>>(
    `/api/notifications/history${q ? `?${q}` : ""}`,
  );
}

export async function sendTestNotification(
  channel: string,
  eventType: string,
): Promise<void> {
  await api.post("/api/notifications/test", { channel, eventType });
}

export async function markNotificationAsRead(id: string): Promise<void> {
  await api.post(`/api/notifications/${id}/read`, {});
}

export async function markNotificationAsUnread(id: string): Promise<void> {
  await api.post(`/api/notifications/${id}/unread`, {});
}

export async function markAllNotificationsAsRead(): Promise<void> {
  await api.post("/api/notifications/read-all", {});
}

export async function fetchUnreadCount(): Promise<number> {
  const data = await api.get<{ count: number }>("/api/notifications/unread-count");
  return data.count;
}
