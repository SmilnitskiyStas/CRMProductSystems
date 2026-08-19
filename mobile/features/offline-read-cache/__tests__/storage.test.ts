import AsyncStorage from '@react-native-async-storage/async-storage';
import {
  offlineReadStorageKey,
  pruneExpiredOfflineReadCaches,
  readOfflineReadCache,
  writeOfflineReadCache,
} from '../storage';
import { OFFLINE_READ_ENVIRONMENT, OFFLINE_READ_SCHEMA_VERSION } from '../policy';
import type { OfflineCacheOwner, PersistedOfflineReadCache } from '../types';

const owner: OfflineCacheOwner = { tenantId: 'tenant-a', userId: 'user-a' };
const other: OfflineCacheOwner = { tenantId: 'tenant-b', userId: 'user-b' };

function record(recordOwner = owner): PersistedOfflineReadCache {
  return { schemaVersion: OFFLINE_READ_SCHEMA_VERSION, environment: OFFLINE_READ_ENVIRONMENT,
    owner: recordOwner, savedAt: 100, entries: [] };
}

describe('offline read storage', () => {
  beforeEach(async () => AsyncStorage.clear());

  test('namespaces by schema, environment, tenant and user', () => {
    expect(offlineReadStorageKey(owner)).not.toBe(offlineReadStorageKey(other));
    expect(offlineReadStorageKey(owner)).toContain(`v${OFFLINE_READ_SCHEMA_VERSION}:production`);
  });

  test.each(['{broken', JSON.stringify({ ...record(), schemaVersion: 99 })])(
    'fails closed and deletes corrupt/version-incompatible current-owner data', async (raw) => {
      await AsyncStorage.setItem(offlineReadStorageKey(owner), raw);
      await expect(readOfflineReadCache(owner)).resolves.toBeNull();
      await expect(AsyncStorage.getItem(offlineReadStorageKey(owner))).resolves.toBeNull();
    });

  test('rejects a foreign record without touching the foreign owner namespace', async () => {
    await AsyncStorage.setItem(offlineReadStorageKey(owner), JSON.stringify(record(other)));
    await AsyncStorage.setItem(offlineReadStorageKey(other), JSON.stringify(record(other)));
    await expect(readOfflineReadCache(owner)).resolves.toBeNull();
    await expect(AsyncStorage.getItem(offlineReadStorageKey(other))).resolves.not.toBeNull();
  });

  test('refuses an owner mismatch on write', async () => {
    await expect(writeOfflineReadCache(owner, record(other))).resolves.toBe(false);
    await expect(AsyncStorage.getItem(offlineReadStorageKey(owner))).resolves.toBeNull();
  });

  test('prunes expired/corrupt namespaces without reading a retained foreign owner into memory', async () => {
    const expired = { ...record(owner), entries: [{ retainedUntil: 99 }] };
    const retained = { ...record(other), entries: [{ retainedUntil: 101 }] };
    await AsyncStorage.setItem(offlineReadStorageKey(owner), JSON.stringify(expired));
    await AsyncStorage.setItem(offlineReadStorageKey(other), JSON.stringify(retained));
    await pruneExpiredOfflineReadCaches(100);
    await expect(AsyncStorage.getItem(offlineReadStorageKey(owner))).resolves.toBeNull();
    await expect(AsyncStorage.getItem(offlineReadStorageKey(other))).resolves.not.toBeNull();
  });

  test('fails closed for an oversized record and tolerates storage-pressure write failure', async () => {
    await AsyncStorage.setItem(offlineReadStorageKey(owner), 'x'.repeat(2 * 1024 * 1024 + 1));
    await expect(readOfflineReadCache(owner)).resolves.toBeNull();
    jest.spyOn(AsyncStorage, 'setItem').mockRejectedValueOnce(new Error('disk full'));
    await expect(writeOfflineReadCache(owner, record())).resolves.toBe(false);
  });
});
