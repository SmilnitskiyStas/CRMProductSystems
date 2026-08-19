import { create } from 'zustand';
import { persistActiveTenantId, readActiveTenantId } from './storage';
import type { ActiveTenantHydrationStatus } from './types';

interface ActiveTenantState {
  activeTenantId: string | null;
  hydrationStatus: ActiveTenantHydrationStatus;
  hydrate: () => Promise<void>;
  setActiveTenantId: (tenantId: string | null) => Promise<void>;
  reset: () => Promise<void>;
}

let hydrationPromise: Promise<void> | null = null;

export const useActiveTenantStore = create<ActiveTenantState>((set) => ({
  activeTenantId: null,
  hydrationStatus: 'idle',

  hydrate: async () => {
    if (hydrationPromise) return hydrationPromise;
    set({ hydrationStatus: 'pending' });
    hydrationPromise = readActiveTenantId()
      .then((activeTenantId) => set({ activeTenantId, hydrationStatus: 'ready' }))
      .catch(() => set({ activeTenantId: null, hydrationStatus: 'error' }))
      .finally(() => {
        hydrationPromise = null;
      });
    return hydrationPromise;
  },

  setActiveTenantId: async (tenantId) => {
    await persistActiveTenantId(tenantId);
    set({ activeTenantId: tenantId?.trim() || null, hydrationStatus: 'ready' });
  },

  reset: async () => {
    await persistActiveTenantId(null);
    set({ activeTenantId: null, hydrationStatus: 'ready' });
  },
}));
