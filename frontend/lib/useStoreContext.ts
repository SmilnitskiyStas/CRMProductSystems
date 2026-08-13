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
}

export const useStoreContext = create<StoreContextState>()(
  persist(
    (set) => ({
      selectedStoreIds: [],
      setSelectedStoreIds: (ids) => set({ selectedStoreIds: ids }),
      initialized: false,
      setInitialized: (v) => set({ initialized: v }),
    }),
    { name: "shelfguard-selected-store" },
  ),
);

/** First selected store, or undefined when "all stores" is chosen (empty selection) or nothing
 * resolved yet. For contexts that need exactly one concrete store — actions, not report filters
 * (dashboard action buttons, stock, POS, single-store analytics endpoints) — not a multi-store
 * filter. */
export function usePrimaryStoreId(): string | undefined {
  return useStoreContext((s) => s.selectedStoreIds[0]);
}
