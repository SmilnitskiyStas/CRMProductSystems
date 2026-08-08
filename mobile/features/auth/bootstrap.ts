import { getMe } from './api/authApi';
import { terminateSession } from './session';
import { useAuthStore } from './store';
import type { AuthUser, ConsumerUser } from './types';
import { queryClient } from '@/lib/query-client';
import { activateOfflineReadCache, getAvailableOfflineReadRoutes } from '@/features/offline-read-cache/lifecycle';
import { persistOfflineCacheOwner, readOfflineCacheOwner } from '@/features/offline-read-cache/owner-pointer';
import { loadOfflineSessionSnapshot } from './offlineSnapshot';

interface BootstrapState {
  personalAccessToken: string | null;
  workspaceAccessToken: string | null;
  user: AuthUser | null;
  consumerUser: ConsumerUser | null;
}

export interface SessionBootstrapDependencies {
  loadPersistedSession: () => Promise<void>;
  getState: () => BootstrapState;
  setStaffUser: (user: AuthUser) => void;
  fetchStaffUser: typeof getMe;
  terminate: () => Promise<void>;
  finishHydration: () => void;
  markRetryable: () => void;
  clearPrivateCache: () => void;
  hydratePrivateCache: (user: AuthUser) => Promise<void>;
  restoreOfflineReads: () => Promise<'ready' | 'missing' | 'invalid'>;
}

export function isTerminalBootstrapAuthError(error: unknown): boolean {
  const status = (error as { response?: { status?: number } })?.response?.status;
  return status === 401 || status === 403;
}

export async function runSessionBootstrap(
  dependencies: SessionBootstrapDependencies
): Promise<void> {
  try {
    await dependencies.loadPersistedSession();
  } catch {
    dependencies.clearPrivateCache();
    dependencies.markRetryable();
    return;
  }

  const state = dependencies.getState();
  if (!state.personalAccessToken && !state.workspaceAccessToken) {
    dependencies.finishHydration();
    return;
  }

  // A personal token with no restorable profile snapshot is a corrupted session state
  // (SecureStore lost/corrupted the JSON, or manual tampering) — nothing safe to show.
  if (state.personalAccessToken && !state.consumerUser) {
    await dependencies.terminate();
    dependencies.finishHydration();
    return;
  }

  // No workspace token to validate — either a plain consumer session (trust the restored
  // consumerUser snapshot; there is no server-side "me" endpoint to re-verify it against,
  // same as before TASK-497) or, after the corruption check above, nothing left to do.
  if (!state.workspaceAccessToken) {
    dependencies.finishHydration();
    return;
  }

  if (!state.user) {
    try {
      const user = await dependencies.fetchStaffUser();
      await dependencies.hydratePrivateCache(user);
      dependencies.setStaffUser(user);
    } catch (error) {
      if (isTerminalBootstrapAuthError(error)) {
        await dependencies.terminate();
        dependencies.finishHydration();
      } else {
        const offline = await dependencies.restoreOfflineReads();
        if (offline === 'ready') return;
        if (offline === 'invalid') {
          await dependencies.terminate();
          dependencies.finishHydration();
        } else {
          dependencies.clearPrivateCache();
          dependencies.markRetryable();
        }
      }
      return;
    }
  } else await dependencies.hydratePrivateCache(state.user);

  dependencies.finishHydration();
}

let bootstrapPromise: Promise<void> | null = null;

export function bootstrapSession(): Promise<void> {
  if (bootstrapPromise) return bootstrapPromise;
  const store = useAuthStore.getState();
  bootstrapPromise = runSessionBootstrap({
    loadPersistedSession: store.loadToken,
    getState: useAuthStore.getState,
    setStaffUser: store.setUser,
    fetchStaffUser: getMe,
    terminate: terminateSession,
    finishHydration: store.finishHydration,
    markRetryable: store.markHydrationRetryable,
    clearPrivateCache: () => queryClient.clear(),
    hydratePrivateCache: async (user) => {
      const owner = { tenantId: user.tenantId ?? 'provider', userId: user.id };
      await persistOfflineCacheOwner(owner);
      await activateOfflineReadCache(queryClient, owner);
    },
    restoreOfflineReads: async () => {
      const result = await loadOfflineSessionSnapshot();
      if (result.status !== 'valid') return result.status;
      const owner = await readOfflineCacheOwner();
      if (!owner || owner.tenantId !== result.snapshot.tenantId || owner.userId !== result.snapshot.userId) {
        return 'invalid';
      }
      await activateOfflineReadCache(queryClient, owner);
      const availableRoutes = getAvailableOfflineReadRoutes(result.snapshot.allowedRoutes);
      if (availableRoutes.length === 0) return 'missing';
      useAuthStore.getState().enterOfflineReadMode({ ...result.snapshot,
        allowedRoutes: availableRoutes as typeof result.snapshot.allowedRoutes });
      return 'ready';
    },
  }).finally(() => {
    bootstrapPromise = null;
  });
  return bootstrapPromise;
}

export function retrySessionBootstrap(): Promise<void> {
  useAuthStore.getState().beginHydration();
  return bootstrapSession();
}
