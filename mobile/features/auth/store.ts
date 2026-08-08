import { create } from 'zustand';
import * as SecureStore from 'expo-secure-store';
import type { AuthUser, ConsumerUser } from './types';
import { queryClient } from '@/lib/query-client';
import { activateOfflineReadCache, clearActiveOfflineReadCache } from '@/features/offline-read-cache/lifecycle';
import { clearOfflineCacheOwner, persistOfflineCacheOwner } from '@/features/offline-read-cache/owner-pointer';

/**
 * TASK-405/407 introduced a device-holds-one-session model (`sessionKind: 'staff' |
 * 'consumer'`, one stored token). TASK-497 replaced it: an employee is first and foremost
 * a normal loyalty-program participant (ConsumerAccount) who *additionally* gets workspace
 * access when linked to an active staff User. Both identities are usable at once, so the
 * store now holds two independent tokens — `personalAccessToken` (consumer/loyalty JWT)
 * and `workspaceAccessToken` (staff/tenant JWT) — either or both of which may be set.
 *
 * `sessionKind` is kept as a lightweight "which identity is richer" signal ('staff' once a
 * workspace token exists, else 'consumer') for anything that still wants a single label, but
 * route guards must not treat it as exclusive — check the token fields directly instead (see
 * (app)/_layout.tsx, (personal)/_layout.tsx, (auth)/_layout.tsx).
 */
export type SessionKind = 'staff' | 'consumer';
export type AuthHydrationStatus = 'pending' | 'ready' | 'retryable_error' | 'offline_read_ready';

const PERSONAL_ACCESS_TOKEN_KEY = 'personal_access_token';
const WORKSPACE_ACCESS_TOKEN_KEY = 'workspace_access_token';
const SESSION_KIND_KEY = 'session_kind';
const CONSUMER_USER_KEY = 'consumer_user';

interface AuthState {
  hydrationStatus: AuthHydrationStatus;
  sessionKind: SessionKind | null;
  personalAccessToken: string | null;
  workspaceAccessToken: string | null;
  user: AuthUser | null;
  consumerUser: ConsumerUser | null;
  twoFactorChallenge: { challengeToken: string; email: string } | null;
  offlineSnapshot: import('./offlineSnapshot').OfflineSessionSnapshot | null;

  /** Full workspace login: token + staff profile. Never touches the personal token/profile —
   * the two identities are additive, not a mutually-exclusive swap. */
  setWorkspaceAuth: (token: string, user: AuthUser) => Promise<void>;
  setUser: (user: AuthUser) => void;
  /** Full personal login: token + consumer profile. */
  setPersonalAuth: (token: string, user: ConsumerUser) => Promise<void>;
  /** Token-only personal grant, used when a 2FA challenge response carries a
   * personalAccessToken but no profile data (see TwoFactorChallengeResponse). */
  setPersonalToken: (token: string) => Promise<void>;
  setConsumerUser: (user: ConsumerUser) => void;
  setTwoFactorChallenge: (challengeToken: string, email: string) => void;
  clearTwoFactorChallenge: () => void;
  clearAuth: () => Promise<void>;
  loadToken: () => Promise<void>;
  finishHydration: () => void;
  beginHydration: () => void;
  markHydrationRetryable: () => void;
  enterOfflineReadMode: (snapshot: import('./offlineSnapshot').OfflineSessionSnapshot) => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  hydrationStatus: 'pending',
  sessionKind: null,
  personalAccessToken: null,
  workspaceAccessToken: null,
  user: null,
  consumerUser: null,
  twoFactorChallenge: null,
  offlineSnapshot: null,

  setWorkspaceAuth: async (token, user) => {
    await SecureStore.setItemAsync(WORKSPACE_ACCESS_TOKEN_KEY, token);
    await SecureStore.setItemAsync(SESSION_KIND_KEY, 'staff');
    const cacheOwner = { tenantId: user.tenantId ?? 'provider', userId: user.id };
    await persistOfflineCacheOwner(cacheOwner);
    await activateOfflineReadCache(queryClient, cacheOwner);
    set({
      workspaceAccessToken: token,
      user,
      sessionKind: 'staff',
      twoFactorChallenge: null,
      offlineSnapshot: null,
    });
  },

  setUser: (user) => set({ user, offlineSnapshot: null }),

  setPersonalAuth: async (token, user) => {
    const hasWorkspace = useAuthStore.getState().workspaceAccessToken !== null;
    await SecureStore.setItemAsync(PERSONAL_ACCESS_TOKEN_KEY, token);
    await SecureStore.setItemAsync(CONSUMER_USER_KEY, JSON.stringify(user));
    if (!hasWorkspace) await SecureStore.setItemAsync(SESSION_KIND_KEY, 'consumer');
    set({
      personalAccessToken: token,
      consumerUser: user,
      sessionKind: hasWorkspace ? 'staff' : 'consumer',
      twoFactorChallenge: null,
    });
  },

  setPersonalToken: async (token) => {
    await SecureStore.setItemAsync(PERSONAL_ACCESS_TOKEN_KEY, token);
    set({ personalAccessToken: token });
  },

  setConsumerUser: (user) => set({ consumerUser: user }),
  setTwoFactorChallenge: (challengeToken, email) =>
    set({ twoFactorChallenge: { challengeToken, email } }),
  clearTwoFactorChallenge: () => set({ twoFactorChallenge: null }),
  finishHydration: () => set({ hydrationStatus: 'ready' }),
  beginHydration: () => set({ hydrationStatus: 'pending', user: null }),
  markHydrationRetryable: () => set({ hydrationStatus: 'retryable_error', user: null }),
  enterOfflineReadMode: (offlineSnapshot) => set({ hydrationStatus: 'offline_read_ready', user: null, offlineSnapshot }),

  clearAuth: async () => {
    // Local identity must be cleared even if one SecureStore operation fails
    // (for example during a device keystore transition). Best-effort removal of
    // every key prevents one failure from skipping the remaining cleanup.
    await Promise.allSettled([
      SecureStore.deleteItemAsync(PERSONAL_ACCESS_TOKEN_KEY),
      SecureStore.deleteItemAsync(WORKSPACE_ACCESS_TOKEN_KEY),
      SecureStore.deleteItemAsync(SESSION_KIND_KEY),
      SecureStore.deleteItemAsync(CONSUMER_USER_KEY),
    ]);
    set({
      personalAccessToken: null,
      workspaceAccessToken: null,
      user: null,
      consumerUser: null,
      sessionKind: null,
      twoFactorChallenge: null,
      offlineSnapshot: null,
    });
  },

  // Restores both persisted tokens (+ sessionKind, + the consumer profile snapshot when a
  // personal token exists) — staff `user` is deliberately NOT restored here. Callers must
  // repopulate it via GET /auth/me afterwards (see bootstrap.ts) or every role-gated staff
  // screen sees a null user until re-login. Consumer sessions have no equivalent "me"
  // endpoint (ConsumerAuthController only exposes register/login), so their display info
  // (fullName/phone) is snapshotted into SecureStore at login time instead and restored
  // verbatim here.
  loadToken: async () => {
    const [personalToken, workspaceToken, rawKind, rawConsumerUser] = await Promise.all([
      SecureStore.getItemAsync(PERSONAL_ACCESS_TOKEN_KEY),
      SecureStore.getItemAsync(WORKSPACE_ACCESS_TOKEN_KEY),
      SecureStore.getItemAsync(SESSION_KIND_KEY),
      SecureStore.getItemAsync(CONSUMER_USER_KEY),
    ]);

    if (!personalToken && !workspaceToken) return;

    let consumerUser: ConsumerUser | null = null;
    if (personalToken && rawConsumerUser) {
      try {
        consumerUser = JSON.parse(rawConsumerUser) as ConsumerUser;
      } catch {
        consumerUser = null;
      }
    }

    const sessionKind: SessionKind = workspaceToken
      ? 'staff'
      : rawKind === 'consumer'
        ? 'consumer'
        : personalToken
          ? 'consumer'
          : 'staff';

    set({
      personalAccessToken: personalToken,
      workspaceAccessToken: workspaceToken,
      sessionKind,
      consumerUser,
    });
  },
}));
