import { createContext, useContext, useEffect, useMemo, useState, type PropsWithChildren } from 'react';
import { useActiveTenant } from '@/features/tenant/ActiveTenantProvider';
import { createMockMobileConfig, SAFE_DEFAULT_TENANT_ID } from './mock';
import { loadMobileConfig, loadPreviewMobileConfig, type MobileConfigSource } from './loader';
import { getPreviewMobileConfig, publishedMobileConfigRepository } from './repository';
import { useMobilePreviewStore } from '@/features/mobile-preview/store';
import type { MobileConfig } from './types';

interface MobileConfigContextValue {
  config: MobileConfig;
  source: MobileConfigSource;
  status: 'loading' | 'ready' | 'fallback';
  error: Error | null;
  preview: boolean;
  cachedAt: number | null;
}

const MobileConfigContext = createContext<MobileConfigContextValue | null>(null);

export function MobileConfigProvider({ children }: PropsWithChildren) {
  const { activeTenantId } = useActiveTenant();
  const tenantId = activeTenantId ?? SAFE_DEFAULT_TENANT_ID;
  const previewToken = useMobilePreviewStore((state) => state.token);
  const [loaded, setLoaded] = useState<MobileConfigContextValue>(() => ({
    config: createMockMobileConfig(tenantId),
    source: 'safe-default' as MobileConfigSource,
    status: 'loading',
    error: null as Error | null,
    preview: false,
    cachedAt: null,
  }));

  useEffect(() => {
    let current = true;
    const request = previewToken && __DEV__
      ? loadPreviewMobileConfig(tenantId, previewToken, getPreviewMobileConfig).then((config) => ({
          config,
          source: 'preview' as MobileConfigSource,
          error: null,
          cachedAt: null,
        }))
      : loadMobileConfig(
          tenantId,
          publishedMobileConfigRepository.getConfig,
          publishedMobileConfigRepository.source
        );
    void request.then((result) => {
      if (!current) return;
      setLoaded({
        config: result.config,
        source: result.source,
        status: ['mock', 'published', 'preview'].includes(result.source) ? 'ready' : 'fallback',
        error: result.error,
        preview: result.source === 'preview',
        cachedAt: result.cachedAt,
      });
    }).catch((cause) => {
      if (!current) return;
      setLoaded({
        config: createMockMobileConfig(tenantId),
        source: 'safe-default',
        status: 'fallback',
        error: cause instanceof Error ? cause : new Error('PREVIEW_LOAD_FAILED'),
        preview: false,
        cachedAt: null,
      });
    });
    return () => {
      current = false;
    };
  }, [previewToken, tenantId]);

  const value = useMemo<MobileConfigContextValue>(
    () =>
      loaded.config.tenant.id === tenantId
        ? loaded
        : {
            config: createMockMobileConfig(tenantId),
            source: 'safe-default',
            status: 'loading',
            error: null,
            preview: false,
            cachedAt: null,
          },
    [loaded, tenantId]
  );

  return <MobileConfigContext.Provider value={value}>{children}</MobileConfigContext.Provider>;
}

export function useMobileConfig(): MobileConfigContextValue {
  const value = useContext(MobileConfigContext);
  if (!value) throw new Error('useMobileConfig must be used within MobileConfigProvider');
  return value;
}
