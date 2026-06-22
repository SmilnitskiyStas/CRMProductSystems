"use client";

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  fetchNotificationSettings,
  updateNotificationSetting,
  fetchNotificationHistory,
  sendTestNotification,
  markNotificationAsRead,
  markAllNotificationsAsRead,
  fetchUnreadCount,
} from "../api/notifications";
import type {
  NotificationSettingsMap,
  NotificationChannel,
  NotificationEventType,
  NotificationHistoryItem,
} from "../types";

// ── Mock data (backend TASK-017 not yet implemented) ──────────────────────────

const MOCK_SETTINGS_MAP: NotificationSettingsMap = {
  "stock.expiry_warning": {
    telegram: { id: "1", isEnabled: true },
    push: { id: "2", isEnabled: true },
  },
  "stock.expiry_critical": {
    telegram: { id: "3", isEnabled: true },
    push: { id: "4", isEnabled: true },
    email: { id: "5", isEnabled: false },
  },
  "stock.expired": {
    telegram: { id: "6", isEnabled: true },
    push: { id: "7", isEnabled: true },
    email: { id: "8", isEnabled: true },
  },
  "stock.needs_verification": {
    telegram: { id: "9", isEnabled: false },
  },
  "weekly_report": {
    email: { id: "10", isEnabled: true },
  },
};

const MOCK_HISTORY: NotificationHistoryItem[] = [
  {
    id: "h1",
    eventType: "stock.expiry_critical",
    channel: "telegram",
    status: "sent",
    payload: "Молоко 2.5% — залишилось 2 дні",
    createdAt: "2026-06-06T08:15:00Z",
    isRead: false,
    readAt: null,
  },
  {
    id: "h2",
    eventType: "stock.expired",
    channel: "email",
    status: "sent",
    payload: "Йогурт полуниця — прострочено",
    createdAt: "2026-06-05T09:00:00Z",
    isRead: true,
    readAt: "2026-06-05T10:00:00Z",
  },
  {
    id: "h3",
    eventType: "weekly_report",
    channel: "email",
    status: "sent",
    payload: "Тижневий звіт за 26.05–01.06.2026",
    createdAt: "2026-06-01T08:00:00Z",
    isRead: true,
    readAt: "2026-06-01T09:00:00Z",
  },
  {
    id: "h4",
    eventType: "stock.expiry_warning",
    channel: "push",
    status: "failed",
    payload: "Сир Гауда — залишилось 8 днів",
    createdAt: "2026-06-04T11:30:00Z",
    isRead: false,
    readAt: null,
  },
];

// ── Hooks ─────────────────────────────────────────────────────────────────────

export function useNotificationSettings() {
  return useQuery<NotificationSettingsMap>({
    queryKey: ["notifications", "settings"],
    queryFn: async () => {
      try {
        const raw = await fetchNotificationSettings();
        return raw.reduce<NotificationSettingsMap>((acc, s) => {
          if (!acc[s.eventType]) acc[s.eventType] = {};
          acc[s.eventType][s.channel as NotificationChannel] = {
            id: s.id,
            isEnabled: s.isEnabled,
          };
          return acc;
        }, {});
      } catch {
        // Fallback to mock while backend is starting up
        return MOCK_SETTINGS_MAP;
      }
    },
    staleTime: 30_000,
  });
}

export function useNotificationHistory() {
  return useQuery<NotificationHistoryItem[]>({
    queryKey: ["notifications", "history"],
    queryFn: async () => {
      try {
        return await fetchNotificationHistory();
      } catch {
        return MOCK_HISTORY;
      }
    },
    staleTime: 15_000,
  });
}

export function useToggleNotification() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      eventType,
      channel,
      isEnabled,
    }: {
      eventType: NotificationEventType;
      channel: NotificationChannel;
      isEnabled: boolean;
    }) => updateNotificationSetting(eventType, channel, isEnabled),

    // Optimistic update
    onMutate: async ({ eventType, channel, isEnabled }) => {
      await qc.cancelQueries({ queryKey: ["notifications", "settings"] });
      const prev = qc.getQueryData<NotificationSettingsMap>(["notifications", "settings"]);
      qc.setQueryData<NotificationSettingsMap>(["notifications", "settings"], (old) => {
        if (!old) return old;
        const next = { ...old };
        if (!next[eventType]) next[eventType] = {};
        const existing = next[eventType][channel];
        next[eventType] = {
          ...next[eventType],
          [channel]: { id: existing?.id ?? "", isEnabled },
        };
        return next;
      });
      return { prev };
    },

    onError: (_err, _vars, ctx) => {
      if (ctx?.prev) {
        qc.setQueryData(["notifications", "settings"], ctx.prev);
      }
    },
    onSettled: () => {
      qc.invalidateQueries({ queryKey: ["notifications", "settings"] });
    },
  });
}

export function useSendTestNotification() {
  return useMutation({
    mutationFn: ({ channel, eventType }: { channel: string; eventType: string }) =>
      sendTestNotification(channel, eventType),
  });
}

const HISTORY_KEY = ["notifications", "history"] as const;
const UNREAD_KEY  = ["notifications", "unread-count"] as const;

export function useUnreadCount() {
  return useQuery<number>({
    queryKey: UNREAD_KEY,
    queryFn: async () => {
      try { return await fetchUnreadCount(); } catch { return 0; }
    },
    staleTime: 30_000,
    refetchInterval: 60_000,
  });
}

export function useMarkAsRead() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => markNotificationAsRead(id),
    onSuccess: (_data, id) => {
      // Optimistic update in history cache
      qc.setQueryData<NotificationHistoryItem[]>(HISTORY_KEY, (old) =>
        old?.map((n) => n.id === id ? { ...n, isRead: true, readAt: new Date().toISOString() } : n)
      );
      qc.invalidateQueries({ queryKey: UNREAD_KEY });
    },
  });
}

export function useMarkAllAsRead() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: markAllNotificationsAsRead,
    onSuccess: () => {
      qc.setQueryData<NotificationHistoryItem[]>(HISTORY_KEY, (old) =>
        old?.map((n) => ({ ...n, isRead: true, readAt: n.readAt ?? new Date().toISOString() }))
      );
      qc.setQueryData(UNREAD_KEY, 0);
    },
  });
}
