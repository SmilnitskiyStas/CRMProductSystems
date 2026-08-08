import AsyncStorage from '@react-native-async-storage/async-storage';
import {
  OFFLINE_READ_ENVIRONMENT,
  OFFLINE_READ_MAX_CACHE_BYTES,
  OFFLINE_READ_SCHEMA_VERSION,
  utf8ByteLength,
} from './policy';
import type { OfflineCacheOwner, PersistedOfflineReadCache } from './types';

const PREFIX = 'shelfguard:offline-read';

export function offlineReadStorageKey(owner: OfflineCacheOwner): string {
  return [PREFIX, `v${OFFLINE_READ_SCHEMA_VERSION}`, OFFLINE_READ_ENVIRONMENT,
    encodeURIComponent(owner.tenantId), encodeURIComponent(owner.userId)].join(':');
}

export function ownersEqual(a: OfflineCacheOwner, b: OfflineCacheOwner): boolean {
  return a.tenantId === b.tenantId && a.userId === b.userId;
}

function isValidRecord(value: unknown, owner: OfflineCacheOwner): value is PersistedOfflineReadCache {
  if (!value || typeof value !== 'object') return false;
  const record = value as Partial<PersistedOfflineReadCache>;
  return record.schemaVersion === OFFLINE_READ_SCHEMA_VERSION
    && record.environment === OFFLINE_READ_ENVIRONMENT
    && !!record.owner && ownersEqual(record.owner, owner)
    && typeof record.savedAt === 'number' && Array.isArray(record.entries);
}

export async function readOfflineReadCache(owner: OfflineCacheOwner): Promise<PersistedOfflineReadCache | null> {
  const key = offlineReadStorageKey(owner);
  let raw: string | null;
  try {
    raw = await AsyncStorage.getItem(key);
  } catch {
    return null;
  }
  if (!raw) return null;
  if (utf8ByteLength(raw) > OFFLINE_READ_MAX_CACHE_BYTES) {
    await AsyncStorage.removeItem(key).catch(() => undefined);
    return null;
  }
  try {
    const parsed: unknown = JSON.parse(raw);
    if (!isValidRecord(parsed, owner)) {
      await AsyncStorage.removeItem(key);
      return null;
    }
    return parsed;
  } catch {
    await AsyncStorage.removeItem(key);
    return null;
  }
}

export async function writeOfflineReadCache(
  owner: OfflineCacheOwner,
  cache: PersistedOfflineReadCache,
): Promise<boolean> {
  if (!ownersEqual(owner, cache.owner)) return false;
  const serialized = JSON.stringify(cache);
  if (utf8ByteLength(serialized) > OFFLINE_READ_MAX_CACHE_BYTES) return false;
  try {
    await AsyncStorage.setItem(offlineReadStorageKey(owner), serialized);
    return true;
  } catch {
    // Storage pressure must not fail authentication/hydration or widen the
    // persistence boundary. The last valid snapshot, if any, stays untouched.
    return false;
  }
}

export async function removeOfflineReadCache(owner: OfflineCacheOwner): Promise<void> {
  await AsyncStorage.removeItem(offlineReadStorageKey(owner));
}

export async function removeAllOfflineReadCaches(): Promise<void> {
  const keys = (await AsyncStorage.getAllKeys()).filter((key) => key.startsWith(`${PREFIX}:`));
  if (keys.length > 0) await AsyncStorage.multiRemove(keys);
}

export async function pruneExpiredOfflineReadCaches(now = Date.now()): Promise<void> {
  try {
    const keys = (await AsyncStorage.getAllKeys()).filter((key) => key.startsWith(`${PREFIX}:`));
    const expired: string[] = [];
    for (const key of keys) {
      const raw = await AsyncStorage.getItem(key);
      if (!raw || utf8ByteLength(raw) > OFFLINE_READ_MAX_CACHE_BYTES) {
        expired.push(key);
        continue;
      }
      try {
        const value = JSON.parse(raw) as Partial<PersistedOfflineReadCache>;
        const hasRetainedEntry = value.schemaVersion === OFFLINE_READ_SCHEMA_VERSION
          && value.environment === OFFLINE_READ_ENVIRONMENT
          && Array.isArray(value.entries)
          && value.entries.some((entry) => typeof entry?.retainedUntil === 'number' && entry.retainedUntil > now);
        if (!hasRetainedEntry) expired.push(key);
      } catch {
        expired.push(key);
      }
    }
    if (expired.length > 0) await AsyncStorage.multiRemove(expired);
  } catch {
    // Best effort under storage/OS pressure; individual owner hydration still
    // validates and rejects expired or corrupt records fail closed.
  }
}
