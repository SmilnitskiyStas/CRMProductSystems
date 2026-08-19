import { createContext, useContext, useEffect, type PropsWithChildren } from 'react';
import { useActiveTenantStore } from './store';
import type { ActiveTenantHydrationStatus } from './types';

interface ActiveTenantContextValue {
  activeTenantId: string | null;
  hydrationStatus: ActiveTenantHydrationStatus;
  setActiveTenantId: (tenantId: string | null) => Promise<void>;
}

const ActiveTenantContext = createContext<ActiveTenantContextValue | null>(null);

export function ActiveTenantProvider({ children }: PropsWithChildren) {
  const activeTenantId = useActiveTenantStore((state) => state.activeTenantId);
  const hydrationStatus = useActiveTenantStore((state) => state.hydrationStatus);
  const hydrate = useActiveTenantStore((state) => state.hydrate);
  const setActiveTenantId = useActiveTenantStore((state) => state.setActiveTenantId);

  useEffect(() => {
    if (hydrationStatus === 'idle') void hydrate();
  }, [hydrate, hydrationStatus]);

  return (
    <ActiveTenantContext.Provider value={{ activeTenantId, hydrationStatus, setActiveTenantId }}>
      {children}
    </ActiveTenantContext.Provider>
  );
}

export function useActiveTenant(): ActiveTenantContextValue {
  const value = useContext(ActiveTenantContext);
  if (!value) throw new Error('useActiveTenant must be used within ActiveTenantProvider');
  return value;
}
