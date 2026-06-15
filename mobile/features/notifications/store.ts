import { create } from 'zustand';

interface NotificationReadState {
  readIds: Set<string>;
  markRead: (id: string) => void;
  markAllRead: (ids: string[]) => void;
  isRead: (id: string) => boolean;
}

export const useNotificationReadStore = create<NotificationReadState>((set, get) => ({
  readIds: new Set<string>(),

  markRead: (id) =>
    set((s) => ({ readIds: new Set([...s.readIds, id]) })),

  markAllRead: (ids) =>
    set((s) => ({ readIds: new Set([...s.readIds, ...ids]) })),

  isRead: (id) => get().readIds.has(id),
}));
