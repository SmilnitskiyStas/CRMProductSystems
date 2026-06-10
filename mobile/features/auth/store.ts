import { create } from 'zustand';
import * as SecureStore from 'expo-secure-store';
import type { AuthUser } from './types';

interface AuthState {
  accessToken: string | null;
  user: AuthUser | null;
  setAuth: (token: string, user: AuthUser) => Promise<void>;
  clearAuth: () => Promise<void>;
  loadToken: () => Promise<void>;
}

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  user: null,

  setAuth: async (token, user) => {
    await SecureStore.setItemAsync('access_token', token);
    set({ accessToken: token, user });
  },

  clearAuth: async () => {
    await SecureStore.deleteItemAsync('access_token');
    set({ accessToken: null, user: null });
  },

  loadToken: async () => {
    const token = await SecureStore.getItemAsync('access_token');
    if (token) {
      set({ accessToken: token });
    }
  },
}));
