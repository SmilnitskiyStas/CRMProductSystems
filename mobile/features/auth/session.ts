import { queryClient } from '@/lib/query-client';
import { useAuthStore } from './store';
import { clearActiveOfflineReadCache } from '@/features/offline-read-cache/lifecycle';
import { clearOfflineCacheOwner, readOfflineCacheOwner } from '@/features/offline-read-cache/owner-pointer';
import { clearOfflineSessionSnapshot } from './offlineSnapshot';

let sessionEpoch = 0;
let terminationPromise: Promise<void> | null = null;

/**
 * A monotonically increasing session generation. Refresh requests capture this
 * before going to the network and must not persist a token if logout happened
 * while they were in flight.
 */
export function getSessionEpoch(): number {
  return sessionEpoch;
}

/** Used by apiClient's (workspace-scoped) refresh-and-retry flow only — the personal
 * client has no refresh mechanism (see mobile/lib/api-client.ts). */
export function updateInMemoryWorkspaceAccessToken(accessToken: string): void {
  useAuthStore.setState({ workspaceAccessToken: accessToken });
}

/**
 * Clears identity and all private server cache exactly once for concurrent
 * terminal 401 responses. Auth layouts react to the cleared Zustand token and
 * perform the appropriate route redirect.
 */
export function terminateSession(): Promise<void> {
  if (terminationPromise) return terminationPromise;

  sessionEpoch += 1;
  const currentUser = useAuthStore.getState().user;
  const cacheOwner = currentUser
    ? { tenantId: currentUser.tenantId ?? 'provider', userId: currentUser.id }
    : null;
  terminationPromise = (async () => {
    try {
      const owner = cacheOwner ?? await readOfflineCacheOwner().catch(() => null);
      await Promise.allSettled([
        useAuthStore.getState().clearAuth(),
        clearActiveOfflineReadCache(owner),
        clearOfflineCacheOwner(),
        clearOfflineSessionSnapshot(),
      ]);
    } finally {
      queryClient.clear();
    }
  })().finally(() => {
    terminationPromise = null;
  });

  return terminationPromise;
}

