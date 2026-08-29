import { create } from 'zustand';

interface WorkspaceLocationState {
  /** undefined = not initialized; null = explicitly show all accessible stores. */
  selectedLocationId: string | null | undefined;
  selectLocation: (locationId: string | null) => void;
  initializeLocation: (locationId: string | null) => void;
  reset: () => void;
}

export const useWorkspaceLocationStore = create<WorkspaceLocationState>((set, get) => ({
  selectedLocationId: undefined,
  selectLocation: (selectedLocationId) => set({ selectedLocationId }),
  initializeLocation: (locationId) => {
    if (get().selectedLocationId === undefined) set({ selectedLocationId: locationId });
  },
  reset: () => set({ selectedLocationId: undefined }),
}));
