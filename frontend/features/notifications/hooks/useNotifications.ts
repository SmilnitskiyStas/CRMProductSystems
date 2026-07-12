"use client";

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  fetchNotificationSettings,
  updateNotificationSetting,
  fetchNotificationHistory,
  sendTestNotification,
  markNotificationAsRead,
  markNotificationAsUnread,
  markAllNotificationsAsRead,
  fetchUnreadCount,
} from "../api/notifications";
import type { PagedResult } from "@/lib/api-types";
import type {
  NotificationSettingsMap,
  NotificationChannel,
  NotificationEventType,
  NotificationHistoryItem,
  NotificationHistoryFilters,
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

// ── Hooks ─────────────────────────────────────────────────────────────────────

export function useNotificationSettings() {
  return useQuery<NotificationSettingsMap>({
    queryKey: ["notifications", "settings"],
    queryFn: async () => {
      const raw = await fetchNotificationSettings();
      return raw.reduce<NotificationSettingsMap>((acc, s) => {
        if (!acc[s.eventType]) acc[s.eventType] = {};
        acc[s.eventType][s.channel as NotificationChannel] = {
          id: s.id,
          isEnabled: s.isEnabled,
        };
        return acc;
      }, {});
    },
    staleTime: 30_000,
    retry: 1,
  });
}

const HISTORY_KEY_PREFIX = ["notifications", "history"] as const;

export function useNotificationHistory(filters: NotificationHistoryFilters = {}) {
  return useQuery<PagedResult<NotificationHistoryItem>>({
    queryKey: [...HISTORY_KEY_PREFIX, filters],
    queryFn: () => fetchNotificationHistory(filters),
    staleTime: 15_000,
    // Keep the previous page's rows on screen while the next page loads —
    // avoids a full-list flash on pagination/filter changes.
    placeholderData: (prev) => prev,
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

// Query key now carries the active filters (`[...HISTORY_KEY_PREFIX, filters]`), so a
// single `setQueryData` can no longer target "the" history cache — there may be several
// filtered variants cached at once. Invalidate by prefix instead; React Query matches
// all queries whose key starts with HISTORY_KEY_PREFIX and refetches the active ones.
export function useMarkAsRead() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => markNotificationAsRead(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: HISTORY_KEY_PREFIX });
      qc.invalidateQueries({ queryKey: UNREAD_KEY });
    },
  });
}

export function useMarkAllAsRead() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: markAllNotificationsAsRead,
    onSuccess: () => {
      qc.setQueryData(UNREAD_KEY, 0);
      qc.invalidateQueries({ queryKey: HISTORY_KEY_PREFIX });
    },
  });
}

export function useMarkAsUnread() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => markNotificationAsUnread(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: HISTORY_KEY_PREFIX });
      qc.invalidateQueries({ queryKey: UNREAD_KEY });
    },
  });
}
