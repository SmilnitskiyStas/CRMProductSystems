import { create } from "zustand";
import { persist } from "zustand/middleware";

interface StoreContextState {
  /** Selected store ids. Empty = all stores. */
  selectedStoreIds: string[];
  setSelectedStoreIds: (ids: string[]) => void;
  /** Whether the one-time default (single concrete store) has ever been resolved. Lets
   * StoreSelector tell "never touched" apart from "user explicitly chose all stores" — both
   * are an empty `selectedStoreIds` array. */
  initialized: boolean;
  setInitialized: (v: boolean) => void;
  /** Whether zustand's persist middleware has finished rehydrating from localStorage.
   * False for one tick on every fresh mount (SSR default, then CSR hydration) — before it
   * flips true, `initialized`/`selectedStoreIds` may still be the in-memory defaults rather
   * than the real persisted values. */
  hasHydrated: boolean;
  setHasHydrated: (v: boolean) => void;
}

export const useStoreContext = create<StoreContextState>()(
  persist(
    (set) => ({
      selectedStoreIds: [],
      setSelectedStoreIds: (ids) => set({ selectedStoreIds: ids }),
      initialized: false,
      setInitialized: (v) => set({ initialized: v }),
      hasHydrated: false,
      setHasHydrated: (v) => set({ hasHydrated: v }),
    }),
    {
      name: "shelfguard-selected-store",
      onRehydrateStorage: () => (state) => state?.setHasHydrated(true),
    },
  ),
);

/** First selected store, or undefined when "all stores" is chosen (empty selection) or nothing
 * resolved yet. For contexts that need exactly one concrete store — actions, not report filters
 * (dashboard action buttons, stock, POS, single-store analytics endpoints) — not a multi-store
 * filter. */
export function usePrimaryStoreId(): string | undefined {
  return useStoreContext((s) => s.selectedStoreIds[0]);
}

/** True once the store scope is trustworthy: persist has hydrated from localStorage AND the
 * one-time default-store resolution has run. Until both are true, `selectedStoreIds`/
 * `usePrimaryStoreId()` may read as empty/undefined for reasons that have nothing to do with the
 * user choosing "all stores" — gate any on-mount fetch that filters by store on this. */
export function useStoreScopeReady(): boolean {
  return useStoreContext((s) => s.hasHydrated && s.initialized);
}
