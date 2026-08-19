import { useEffect } from 'react';
import { useLoyaltyUiStore } from '@/features/loyalty/store';
import { useActiveTenant } from './ActiveTenantProvider';
import { useSwitchActiveTenant } from './useSwitchActiveTenant';

/**
 * Stage 1 compatibility adapter. New retail code reads the global active tenant while
 * existing loyalty screens can keep using selectedTenantId until Stage 2 migrates them.
 */
export function LoyaltyTenantBridge() {
  const { activeTenantId, hydrationStatus } = useActiveTenant();
  const switchTenant = useSwitchActiveTenant();
  const selectedTenantId = useLoyaltyUiStore((state) => state.selectedTenantId);
  const setSelectedTenantId = useLoyaltyUiStore((state) => state.setSelectedTenantId);

  useEffect(() => {
    if (hydrationStatus !== 'ready') return;
    if (selectedTenantId && selectedTenantId !== activeTenantId) {
      void switchTenant(selectedTenantId);
    } else if (!selectedTenantId && activeTenantId) {
      setSelectedTenantId(activeTenantId);
    }
  }, [activeTenantId, hydrationStatus, selectedTenantId, setSelectedTenantId, switchTenant]);

  return null;
}
