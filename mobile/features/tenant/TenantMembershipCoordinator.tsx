import { useEffect } from 'react';
import { useAuthStore } from '@/features/auth/store';
import { useMemberships } from '@/features/loyalty/hooks/useLoyalty';
import { useLoyaltyUiStore } from '@/features/loyalty/store';
import { useActiveTenant } from './ActiveTenantProvider';
import { useSwitchActiveTenant } from './useSwitchActiveTenant';
import { resolveActiveTenantId } from './reconciliation';

export function TenantMembershipCoordinator() {
  const hasPersonalAccess = useAuthStore((state) => state.personalAccessToken !== null);
  const { data: memberships, isLoading } = useMemberships(hasPersonalAccess);
  const { activeTenantId, hydrationStatus } = useActiveTenant();
  const selectedTenantId = useLoyaltyUiStore((state) => state.selectedTenantId);
  const setSelectedTenantId = useLoyaltyUiStore((state) => state.setSelectedTenantId);
  const switchTenant = useSwitchActiveTenant();

  useEffect(() => {
    if (!hasPersonalAccess || hydrationStatus !== 'ready' || isLoading || !memberships) return;
    const nextTenantId = resolveActiveTenantId(memberships, activeTenantId, selectedTenantId);

    if (nextTenantId !== activeTenantId) {
      if (selectedTenantId !== nextTenantId) setSelectedTenantId(nextTenantId);
      void switchTenant(nextTenantId);
      return;
    }
    if (selectedTenantId !== nextTenantId) setSelectedTenantId(nextTenantId);
  }, [
    activeTenantId,
    hasPersonalAccess,
    hydrationStatus,
    isLoading,
    memberships,
    selectedTenantId,
    setSelectedTenantId,
    switchTenant,
  ]);

  return null;
}
