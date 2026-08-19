import AsyncStorage from '@react-native-async-storage/async-storage';
import type { MobileConfig } from './types';
import { validateMobileConfig } from './validation';

const PREFIX = 'retail.mobile-config.last-valid.v0';

interface StoredMobileConfigEnvelope {
  cachedAt: number;
  config: MobileConfig;
}

export interface LastValidMobileConfigEntry {
  config: MobileConfig;
  cachedAt: number | null;
}

export function mobileConfigStorageKey(tenantId: string): string {
  return `${PREFIX}:${encodeURIComponent(tenantId)}`;
}

export async function readLastValidMobileConfig(tenantId: string): Promise<MobileConfig | null> {
  return (await readLastValidMobileConfigEntry(tenantId))?.config ?? null;
}

export async function readLastValidMobileConfigEntry(
  tenantId: string
): Promise<LastValidMobileConfigEntry | null> {
  const key = mobileConfigStorageKey(tenantId);
  const raw = await AsyncStorage.getItem(key);
  if (!raw) return null;

  try {
    const parsed = JSON.parse(raw) as MobileConfig | StoredMobileConfigEnvelope;
    const envelope = 'config' in parsed && 'cachedAt' in parsed ? parsed : null;
    const candidate = envelope?.config ?? parsed;
    const result = validateMobileConfig(candidate);
    if (!result.valid || result.config?.tenant.id !== tenantId) {
      await AsyncStorage.removeItem(key);
      return null;
    }
    return {
      config: result.config,
      cachedAt: envelope && Number.isFinite(envelope.cachedAt) ? envelope.cachedAt : null,
    };
  } catch {
    await AsyncStorage.removeItem(key);
    return null;
  }
}

export async function persistLastValidMobileConfig(
  config: MobileConfig,
  cachedAt = Date.now()
): Promise<void> {
  const result = validateMobileConfig(config);
  if (!result.valid || !result.config) throw new Error('INVALID_MOBILE_CONFIG');
  const envelope: StoredMobileConfigEnvelope = { cachedAt, config: result.config };
  await AsyncStorage.setItem(mobileConfigStorageKey(config.tenant.id), JSON.stringify(envelope));
}
