import AsyncStorage from '@react-native-async-storage/async-storage';
import NetInfo from '@react-native-community/netinfo';
import { QueryClient } from '@tanstack/react-query';
import {
  activateOfflineReadCache,
  clearActiveOfflineReadCache,
  getOfflineReadMetadata,
  persistOfflineReadsNow,
  resetOfflineReadCacheForTests,
} from '../lifecycle';
import { OFFLINE_READ_ENVIRONMENT, OFFLINE_READ_RETENTION_MS, OFFLINE_READ_SCHEMA_VERSION } from '../policy';
import { offlineReadStorageKey } from '../storage';
import type { OfflineCacheOwner, PersistedOfflineReadCache } from '../types';

jest.mock('@react-native-community/netinfo', () => ({
  __esModule: true,
  default: { addEventListener: jest.fn(() => jest.fn()) },
}));

const ownerA: OfflineCacheOwner = { tenantId: 'tenant-a', userId: 'user-a' };
const ownerB: OfflineCacheOwner = { tenantId: 'tenant-b', userId: 'user-b' };
const schedule = [{ id: 's1', locationId: 'l1', locationName: 'One', name: 'Week',
  weekStart: '2026-08-01', status: 'published', shiftCount: 1, createdAt: '2026-08-01' }];

function persisted(owner: OfflineCacheOwner, lastSyncedAt: number): PersistedOfflineReadCache {
  return { schemaVersion: OFFLINE_READ_SCHEMA_VERSION, environment: OFFLINE_READ_ENVIRONMENT,
    owner, savedAt: lastSyncedAt, entries: [{ family: 'schedules', queryKey: ['schedules', null, null], data: schedule,
      lastSyncedAt, softExpiresAt: lastSyncedAt + 24 * 60 * 60 * 1000,
      retainedUntil: lastSyncedAt + OFFLINE_READ_RETENTION_MS, isStale: false }] };
}

describe('offline read lifecycle', () => {
  beforeEach(async () => {
    resetOfflineReadCacheForTests();
    await AsyncStorage.clear();
    jest.clearAllMocks();
  });

  afterEach(() => {
    resetOfflineReadCacheForTests();
    jest.restoreAllMocks();
  });

  test('persists allowlisted summaries only and tracks last sync metadata', async () => {
    const client = new QueryClient();
    await activateOfflineReadCache(client, ownerA, 1_000);
    client.setQueryData(['schedules', undefined, undefined], schedule, { updatedAt: 1_000 });
    client.setQueryData(['auth'], { accessToken: 'secret' }, { updatedAt: 1_000 });
    client.setQueryData(['loyalty', 'code'], { qr: 'rotating-secret' }, { updatedAt: 1_000 });
    await persistOfflineReadsNow(1_001);
    const raw = await AsyncStorage.getItem(offlineReadStorageKey(ownerA));
    expect(raw).not.toMatch(/accessToken|rotating-secret/);
    expect(JSON.parse(raw!).entries).toHaveLength(1);
    expect(getOfflineReadMetadata(['schedules', undefined, undefined], 1_001)?.lastSyncedAt).toBe(1_000);
  });

  test('hydrates retained stale data, drops hard-expired data, and preserves timestamps', async () => {
    const staleAt = 1_000;
    await AsyncStorage.setItem(offlineReadStorageKey(ownerA), JSON.stringify(persisted(ownerA, staleAt)));
    const client = new QueryClient();
    await activateOfflineReadCache(client, ownerA, staleAt + 2 * 24 * 60 * 60 * 1000);
    expect(client.getQueryData(['schedules', null, null])).toEqual(schedule);
    expect(getOfflineReadMetadata(['schedules', null, null], staleAt + 2 * 24 * 60 * 60 * 1000)?.isStale).toBe(true);
    expect(client.getQueryState(['schedules', null, null])?.dataUpdatedAt).toBe(staleAt);

    await activateOfflineReadCache(client, ownerA, staleAt + OFFLINE_READ_RETENTION_MS + 1);
    expect(client.getQueryData(['schedules', null, null])).toBeUndefined();
    await expect(AsyncStorage.getItem(offlineReadStorageKey(ownerA))).resolves.toBeNull();
  });

  test('switching owners hides the old cache before the new hydration resolves', async () => {
    const client = new QueryClient();
    await activateOfflineReadCache(client, ownerA, 1_000);
    client.setQueryData(['schedules', undefined, undefined], schedule);
    let release!: () => void;
    const delayedRead = jest.fn(() => new Promise<null>((resolve) => { release = () => resolve(null); }));
    const switching = activateOfflineReadCache(client, ownerB, 1_000, delayedRead);
    expect(client.getQueryData(['schedules', undefined, undefined])).toBeUndefined();
    while (!release) await new Promise((resolve) => setTimeout(resolve, 0));
    release();
    await switching;
  });

  test('logout clears only the current owner persisted and in-memory cache', async () => {
    const client = new QueryClient();
    await AsyncStorage.setItem(offlineReadStorageKey(ownerB), JSON.stringify(persisted(ownerB, 1_000)));
    await activateOfflineReadCache(client, ownerA, 1_000);
    client.setQueryData(['schedules', undefined, undefined], schedule);
    await persistOfflineReadsNow(1_001);
    await clearActiveOfflineReadCache(ownerA);
    expect(client.getQueryData(['schedules', undefined, undefined])).toBeUndefined();
    await expect(AsyncStorage.getItem(offlineReadStorageKey(ownerA))).resolves.toBeNull();
    await expect(AsyncStorage.getItem(offlineReadStorageKey(ownerB))).resolves.not.toBeNull();
  });

  test('terminal cleanup with an unavailable/corrupt owner pointer clears every read-cache namespace', async () => {
    const client = new QueryClient();
    await AsyncStorage.setItem(offlineReadStorageKey(ownerA), JSON.stringify(persisted(ownerA, 1_000)));
    await AsyncStorage.setItem(offlineReadStorageKey(ownerB), JSON.stringify(persisted(ownerB, 1_000)));
    await activateOfflineReadCache(client, ownerA, 1_000);
    await clearActiveOfflineReadCache(null);
    expect(client.getQueryData(['schedules', null, null])).toBeUndefined();
    await expect(AsyncStorage.getItem(offlineReadStorageKey(ownerA))).resolves.toBeNull();
    await expect(AsyncStorage.getItem(offlineReadStorageKey(ownerB))).resolves.toBeNull();
  });

  test('reconnect invalidates only allowlisted reads and online success refreshes persistence', async () => {
    let listener!: (state: { isConnected: boolean; isInternetReachable: boolean }) => void;
    (NetInfo.addEventListener as jest.Mock).mockImplementation((callback) => { listener = callback; return jest.fn(); });
    const client = new QueryClient();
    await activateOfflineReadCache(client, ownerA, 1_000);
    client.setQueryData(['schedules', undefined, undefined], schedule, { updatedAt: 1_000 });
    client.setQueryData(['auth'], { secret: true }, { updatedAt: 1_000 });
    const invalidate = jest.spyOn(client, 'invalidateQueries');
    listener({ isConnected: false, isInternetReachable: false });
    listener({ isConnected: true, isInternetReachable: true });
    expect(invalidate).toHaveBeenCalledTimes(1);

    client.setQueryData(['schedules', undefined, undefined], [{ ...schedule[0], name: 'Fresh' }], { updatedAt: 5_000 });
    await persistOfflineReadsNow(5_001);
    const raw = JSON.parse((await AsyncStorage.getItem(offlineReadStorageKey(ownerA)))!);
    expect(raw.entries[0].lastSyncedAt).toBe(5_000);
    expect(raw.entries[0].data[0].name).toBe('Fresh');
  });
});
