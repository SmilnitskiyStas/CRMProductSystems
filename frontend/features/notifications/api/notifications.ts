import { api } from "@/lib/api";
import type { NotificationSetting, NotificationHistoryItem } from "../types";

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

export async function fetchNotificationHistory(): Promise<NotificationHistoryItem[]> {
  return api.get<NotificationHistoryItem[]>("/api/notifications/history");
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

export async function markAllNotificationsAsRead(): Promise<void> {
  await api.post("/api/notifications/read-all", {});
}

export async function fetchUnreadCount(): Promise<number> {
  const data = await api.get<{ count: number }>("/api/notifications/unread-count");
  return data.count;
}
