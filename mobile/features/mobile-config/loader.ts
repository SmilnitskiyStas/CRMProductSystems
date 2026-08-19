import { createMockMobileConfig } from './mock';
import { readLastValidMobileConfigEntry, persistLastValidMobileConfig } from './storage';
import type { MobileConfig } from './types';
import { validateMobileConfig } from './validation';

export type MobileConfigSource = 'mock' | 'published' | 'last-valid' | 'safe-default';

export interface LoadedMobileConfig {
  config: MobileConfig;
  source: MobileConfigSource;
  error: Error | null;
  cachedAt: number | null;
}

export async function loadMobileConfig(
  tenantId: string,
  getConfig: (tenantId: string) => Promise<unknown>,
  successSource: 'mock' | 'published' = 'mock'
): Promise<LoadedMobileConfig> {
  try {
    const candidate = await getConfig(tenantId);
    const result = validateMobileConfig(candidate);
    if (!result.valid || !result.config || result.config.tenant.id !== tenantId) {
      const details = result.errors
        .slice(0, 5)
        .map((item) => `${item.instancePath || '/'} ${item.message ?? 'is invalid'}`)
        .join('; ');
      throw new Error(details ? `INVALID_MOBILE_CONFIG: ${details}` : 'INVALID_MOBILE_CONFIG');
    }
    const cachedAt = Date.now();
    await persistLastValidMobileConfig(result.config, cachedAt);
    return { config: result.config, source: successSource, error: null, cachedAt };
  } catch (cause) {
    const error = cause instanceof Error ? cause : new Error('MOBILE_CONFIG_LOAD_FAILED');
    const cached = await readLastValidMobileConfigEntry(tenantId);
    if (cached) return { config: cached.config, source: 'last-valid', error, cachedAt: cached.cachedAt };
    return { config: createMockMobileConfig(tenantId), source: 'safe-default', error, cachedAt: null };
  }
}
