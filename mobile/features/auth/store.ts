import { create } from 'zustand';
import * as SecureStore from 'expo-secure-store';
import type { AuthUser, ConsumerUser } from './types';

/**
 * TASK-405/407: a device holds exactly one active session at a time — either a staff
 * User (tenant-scoped, existing flow) or a ConsumerAccount (global loyalty wallet,
 * cross-tenant, no tenant_id). `sessionKind` is the discriminator the root layout and
 * (app)/(consumer) group guards branch on; `user` (staff) and `consumerUser` (consumer)
 * are kept as separate fields rather than a union so every existing staff call site
 * (`user?.role`, `user?.email`, ...) keeps compiling untouched.
 */
export type SessionKind = 'staff' | 'consumer';

const ACCESS_TOKEN_KEY = 'access_token';
const SESSION_KIND_KEY = 'session_kind';
const CONSUMER_USER_KEY = 'consumer_user';

interface AuthState {
  sessionKind: SessionKind | null;
  accessToken: string | null;
  user: AuthUser | null;
  consumerUser: ConsumerUser | null;

  setAuth: (token: string, user: AuthUser) => Promise<void>;
  setUser: (user: AuthUser) => void;
  setConsumerAuth: (token: string, user: ConsumerUser) => Promise<void>;
  setConsumerUser: (user: ConsumerUser) => void;
  clearAuth: () => Promise<void>;
  loadToken: () => Promise<void>;
}

export const useAuthStore = create<AuthState>((set) => ({
  sessionKind: null,
  accessToken: null,
  user: null,
  consumerUser: null,

  setAuth: async (token, user) => {
    await SecureStore.setItemAsync(ACCESS_TOKEN_KEY, token);
    await SecureStore.setItemAsync(SESSION_KIND_KEY, 'staff');
    // A device switching from a previous consumer session to a staff login shouldn't
    // leave a stale wallet snapshot behind.
    await SecureStore.deleteItemAsync(CONSUMER_USER_KEY);
    set({ accessToken: token, user, consumerUser: null, sessionKind: 'staff' });
  },

  setUser: (user) => set({ user }),

  setConsumerAuth: async (token, user) => {
    await SecureStore.setItemAsync(ACCESS_TOKEN_KEY, token);
    await SecureStore.setItemAsync(SESSION_KIND_KEY, 'consumer');
    await SecureStore.setItemAsync(CONSUMER_USER_KEY, JSON.stringify(user));
    set({ accessToken: token, consumerUser: user, user: null, sessionKind: 'consumer' });
  },

  setConsumerUser: (user) => set({ consumerUser: user }),

  clearAuth: async () => {
    await SecureStore.deleteItemAsync(ACCESS_TOKEN_KEY);
    await SecureStore.deleteItemAsync(SESSION_KIND_KEY);
    await SecureStore.deleteItemAsync(CONSUMER_USER_KEY);
    set({ accessToken: null, user: null, consumerUser: null, sessionKind: null });
  },

  // Restores the persisted token (+ sessionKind, + the consumer profile snapshot when
  // applicable) — `user` (staff) is deliberately NOT restored here. Callers must
  // repopulate it via GET /auth/me afterwards (see app/_layout.tsx) or every role-gated
  // staff screen sees a null user until re-login. Consumer sessions have no equivalent
  // "me" endpoint (ConsumerAuthController only exposes register/login), so their display
  // info (fullName/phone) is snapshotted into SecureStore at login time instead and
  // restored verbatim here.
  loadToken: async () => {
    const token = await SecureStore.getItemAsync(ACCESS_TOKEN_KEY);
    if (!token) return;

    const rawKind = await SecureStore.getItemAsync(SESSION_KIND_KEY);
    // Sessions persisted before this task shipped have no session_kind saved yet —
    // treat them as staff, the only session kind that existed then.
    const sessionKind: SessionKind = rawKind === 'consumer' ? 'consumer' : 'staff';

    if (sessionKind === 'consumer') {
      const rawUser = await SecureStore.getItemAsync(CONSUMER_USER_KEY);
      let consumerUser: ConsumerUser | null = null;
      if (rawUser) {
        try {
          consumerUser = JSON.parse(rawUser) as ConsumerUser;
        } catch {
          consumerUser = null;
        }
      }
      set({ accessToken: token, sessionKind, consumerUser });
      return;
    }

    set({ accessToken: token, sessionKind });
  },
}));
