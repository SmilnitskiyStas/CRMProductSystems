import { useCallback } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useActiveTenantStore } from './store';
import { clearTenantQueries } from './queryIsolation';
import { trackConsumerEvent } from '@/features/consumer-analytics/analytics';

export function useSwitchActiveTenant() {
  const queryClient = useQueryClient();
  const activeTenantId = useActiveTenantStore((state) => state.activeTenantId);
  const setActiveTenantId = useActiveTenantStore((state) => state.setActiveTenantId);

  return useCallback(
    async (tenantId: string | null) => {
      if (tenantId === activeTenantId) return;
      if (activeTenantId) await clearTenantQueries(queryClient, activeTenantId);
      await setActiveTenantId(tenantId);
      if (tenantId) void trackConsumerEvent('tenant_selected', tenantId, { source: 'membership_switch' });
    },
    [activeTenantId, queryClient, setActiveTenantId]
  );
}
