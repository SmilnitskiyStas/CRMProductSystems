import { create } from 'zustand';

/**
 * UI-only state (CLAUDE.md: Zustand is for UI state, React Query owns server state) — which
 * tenant's membership is currently selected in the consumer wallet. Shared between
 * (personal)/wallet.tsx and (personal)/history.tsx so switching tabs doesn't reset the
 * selection back to the first membership every time.
 */
interface LoyaltyUiState {
  selectedTenantId: string | null;
  setSelectedTenantId: (tenantId: string | null) => void;
}

export const useLoyaltyUiStore = create<LoyaltyUiState>((set) => ({
  selectedTenantId: null,
  setSelectedTenantId: (tenantId) => set({ selectedTenantId: tenantId }),
}));
