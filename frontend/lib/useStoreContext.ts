import { create } from "zustand";
import { persist } from "zustand/middleware";

interface StoreContextState {
  selectedStoreId: string | null;
  setSelectedStoreId: (id: string) => void;
}

export const useStoreContext = create<StoreContextState>()(
  persist(
    (set) => ({
      selectedStoreId: null,
      setSelectedStoreId: (id) => set({ selectedStoreId: id }),
    }),
    { name: "shelfguard-selected-store" },
  ),
);
