import { create } from 'zustand';
import { canEnableMobilePreview } from './policy';

interface MobilePreviewState {
  token: string | null;
  enable: (token: string) => boolean;
  disable: () => void;
}

export const useMobilePreviewStore = create<MobilePreviewState>((set) => ({
  token: null,
  enable: (token) => {
    if (!canEnableMobilePreview(__DEV__, token)) return false;
    set({ token: token.trim() });
    return true;
  },
  disable: () => set({ token: null }),
}));
