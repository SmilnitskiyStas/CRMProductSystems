import { createContext, useCallback, useContext, useEffect, useMemo, useState, type PropsWithChildren } from 'react';
import { AppState } from 'react-native';
import { useActiveTenant } from '@/features/tenant/ActiveTenantProvider';
import { createMockMobileConfig, SAFE_DEFAULT_TENANT_ID } from './mock';
import { loadMobileConfig, type MobileConfigSource } from './loader';
import { publishedMobileConfigRepository } from './repository';
import type { MobileConfig } from './types';

interface MobileConfigContextValue {
  config: MobileConfig;
  source: MobileConfigSource;
  status: 'loading' | 'ready' | 'fallback';
  error: Error | null;
  cachedAt: number | null;
  refresh: () => void;
}

const MobileConfigContext = createContext<MobileConfigContextValue | null>(null);

export function MobileConfigProvider({ children }: PropsWithChildren) {
  const { activeTenantId } = useActiveTenant();
  const tenantId = activeTenantId ?? SAFE_DEFAULT_TENANT_ID;
  const [refreshVersion, setRefreshVersion] = useState(0);
  const refresh = useCallback(() => setRefreshVersion((version) => version + 1), []);
  const [loaded, setLoaded] = useState<MobileConfigContextValue>(() => ({
    config: createMockMobileConfig(tenantId),
    source: 'safe-default' as MobileConfigSource,
    status: 'loading',
    error: null as Error | null,
    cachedAt: null,
    refresh,
  }));

  useEffect(() => {
    const subscription = AppState.addEventListener('change', (state) => {
      if (state === 'active') refresh();
    });
    return () => subscription.remove();
  }, [refresh]);

  useEffect(() => {
    let current = true;
    const request = loadMobileConfig(
      tenantId,
      publishedMobileConfigRepository.getConfig,
      publishedMobileConfigRepository.source
    );
    void request.then((result) => {
      if (!current) return;
      if (__DEV__ && result.error) {
        console.warn('[mobile-config] Fresh configuration unavailable', result.error);
      }
      setLoaded({
        config: result.config,
        source: result.source,
        status: ['mock', 'published'].includes(result.source) ? 'ready' : 'fallback',
        error: result.error,
        cachedAt: result.cachedAt,
        refresh,
      });
    }).catch((cause) => {
      if (!current) return;
      if (__DEV__) console.warn('[mobile-config] Configuration load failed', cause);
      setLoaded({
        config: createMockMobileConfig(tenantId),
        source: 'safe-default',
        status: 'fallback',
        error: cause instanceof Error ? cause : new Error('MOBILE_CONFIG_LOAD_FAILED'),
        cachedAt: null,
        refresh,
      });
    });
    return () => {
      current = false;
    };
  }, [refresh, refreshVersion, tenantId]);

  const value = useMemo<MobileConfigContextValue>(
    () =>
      loaded.config.tenant.id === tenantId
        ? loaded
        : {
            config: createMockMobileConfig(tenantId),
            source: 'safe-default',
            status: 'loading',
            error: null,
            cachedAt: null,
            refresh,
          },
    [loaded, refresh, tenantId]
  );

  return <MobileConfigContext.Provider value={value}>{children}</MobileConfigContext.Provider>;
}

export function useMobileConfig(): MobileConfigContextValue {
  const value = useContext(MobileConfigContext);
  if (!value) throw new Error('useMobileConfig must be used within MobileConfigProvider');
  return value;
}
