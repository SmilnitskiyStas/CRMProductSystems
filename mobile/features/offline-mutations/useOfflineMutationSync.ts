import { useEffect, useRef } from 'react';
import { useNetInfo } from '@react-native-community/netinfo';
import { queryClient } from '@/lib/query-client';
import { useAuthStore } from '@/features/auth/store';
import { syncQueuedWriteOffs } from './writeOffQueue';
import { syncOperationalMutations } from './operationalQueue';

export function useOfflineMutationSync(): void {
  const network = useNetInfo();
  const user = useAuthStore((state) => state.user);
  const syncing = useRef(false);

  useEffect(() => {
    const online = network.isConnected === true && network.isInternetReachable !== false;
    if (!online || !user?.tenantId || syncing.current) return;
    syncing.current = true;
    const owner = { tenantId: user.tenantId, userId: user.id };
    void Promise.all([syncQueuedWriteOffs(owner), syncOperationalMutations(owner)])
      .then(([writeOffs, operational]) => {
        if (writeOffs.synced > 0) void queryClient.invalidateQueries({ queryKey: ['write-offs'] });
        if (operational.synced > 0) {
          void queryClient.invalidateQueries({ queryKey: ['transfers'] });
          void queryClient.invalidateQueries({ queryKey: ['production-orders'] });
          void queryClient.invalidateQueries({ queryKey: ['receipts'] });
        }
      })
      .finally(() => { syncing.current = false; });
  }, [network.isConnected, network.isInternetReachable, user?.id, user?.tenantId]);
}
