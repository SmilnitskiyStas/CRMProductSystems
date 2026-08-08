import NetInfo, { type NetInfoState } from '@react-native-community/netinfo';
import type { Query, QueryClient, QueryKey } from '@tanstack/react-query';
import {
  getOfflineReadFamily,
  getOfflineReadSoftTtl,
  OFFLINE_READ_ENVIRONMENT,
  OFFLINE_READ_MAX_ENTRY_BYTES,
  OFFLINE_READ_RETENTION_MS,
  OFFLINE_READ_SCHEMA_VERSION,
  sanitizeOfflineReadData,
  utf8ByteLength,
} from './policy';
import {
  ownersEqual,
  pruneExpiredOfflineReadCaches,
  readOfflineReadCache,
  removeAllOfflineReadCaches,
  removeOfflineReadCache,
  writeOfflineReadCache,
} from './storage';
import type { OfflineCacheOwner, OfflineReadMetadata, PersistedOfflineReadCache } from './types';

let activeOwner: OfflineCacheOwner | null = null;
let activeClient: QueryClient | null = null;
let unsubscribeQuery: (() => void) | null = null;
let unsubscribeNetwork: (() => void) | null = null;
let persistTimer: ReturnType<typeof setTimeout> | null = null;
let writeChain = Promise.resolve();
let wasOnline: boolean | null = null;
const metadata = new Map<string, OfflineReadMetadata>();

function metadataKey(queryKey: QueryKey): string {
  return JSON.stringify(queryKey);
}

function networkIsOnline(state: NetInfoState): boolean {
  return state.isConnected === true && state.isInternetReachable !== false;
}

function currentOwnerMatches(owner: OfflineCacheOwner): boolean {
  return !!activeOwner && ownersEqual(activeOwner, owner);
}

function entryFromQuery(query: Query, now: number) {
  const family = getOfflineReadFamily(query.queryKey);
  if (!family || query.state.status !== 'success' || query.state.dataUpdatedAt <= 0) return null;
  const data = sanitizeOfflineReadData(family, query.state.data);
  if (data === null) return null;
  const candidate = {
    family,
    queryKey: query.queryKey,
    data,
    lastSyncedAt: query.state.dataUpdatedAt,
    softExpiresAt: query.state.dataUpdatedAt + getOfflineReadSoftTtl(family),
    retainedUntil: query.state.dataUpdatedAt + OFFLINE_READ_RETENTION_MS,
    isStale: now > query.state.dataUpdatedAt + getOfflineReadSoftTtl(family),
  };
  return utf8ByteLength(JSON.stringify(candidate)) <= OFFLINE_READ_MAX_ENTRY_BYTES
    ? candidate : null;
}

export async function persistOfflineReadsNow(now = Date.now()): Promise<void> {
  const owner = activeOwner;
  const client = activeClient;
  if (!owner || !client) return;
  const entries = client.getQueryCache().getAll()
    .map((query) => entryFromQuery(query, now))
    .filter((entry): entry is NonNullable<typeof entry> => !!entry && entry.retainedUntil > now);
  const cache: PersistedOfflineReadCache = {
    schemaVersion: OFFLINE_READ_SCHEMA_VERSION,
    environment: OFFLINE_READ_ENVIRONMENT,
    owner,
    savedAt: now,
    entries,
  };
  writeChain = writeChain.then(async () => {
    if (currentOwnerMatches(owner)) await writeOfflineReadCache(owner, cache);
  });
  await writeChain;
}

function schedulePersistence(): void {
  if (persistTimer) clearTimeout(persistTimer);
  persistTimer = setTimeout(() => {
    persistTimer = null;
    void persistOfflineReadsNow().catch(() => undefined);
  }, 50);
}

function stopLifecycle(): void {
  unsubscribeQuery?.();
  unsubscribeNetwork?.();
  unsubscribeQuery = null;
  unsubscribeNetwork = null;
  if (persistTimer) clearTimeout(persistTimer);
  persistTimer = null;
  wasOnline = null;
  metadata.clear();
}

export async function activateOfflineReadCache(
  client: QueryClient,
  owner: OfflineCacheOwner,
  now = Date.now(),
  readCache: typeof readOfflineReadCache = readOfflineReadCache,
): Promise<void> {
  stopLifecycle();
  activeOwner = owner;
  activeClient = client;
  client.clear();

  await pruneExpiredOfflineReadCaches(now);
  if (!currentOwnerMatches(owner)) return;
  const persisted = await readCache(owner);
  if (!currentOwnerMatches(owner)) return;
  if (persisted) {
    const safeEntries = [];
    for (const entry of persisted.entries) {
      const family = getOfflineReadFamily(entry.queryKey);
      if (!family || family !== entry.family || typeof entry.lastSyncedAt !== 'number'
        || typeof entry.softExpiresAt !== 'number' || typeof entry.retainedUntil !== 'number'
        || entry.retainedUntil <= now || entry.lastSyncedAt <= 0) continue;
      const safeData = sanitizeOfflineReadData(family, entry.data);
      if (safeData === null) continue;
      safeEntries.push({ ...entry, family, data: safeData });
      client.setQueryData(entry.queryKey, safeData, { updatedAt: entry.lastSyncedAt });
      metadata.set(metadataKey(entry.queryKey), {
        family,
        lastSyncedAt: entry.lastSyncedAt,
        softExpiresAt: entry.softExpiresAt,
        retainedUntil: entry.retainedUntil,
        isStale: now > entry.softExpiresAt,
      });
    }
    if (safeEntries.length !== persisted.entries.length) {
      if (safeEntries.length === 0) await removeOfflineReadCache(owner);
      else await writeOfflineReadCache(owner, { ...persisted, savedAt: now, entries: safeEntries });
    }
  }

  unsubscribeQuery = client.getQueryCache().subscribe((event) => {
    if (event.type !== 'updated' || event.query.state.status !== 'success') return;
    const family = getOfflineReadFamily(event.query.queryKey);
    if (!family) return;
    const updatedAt = event.query.state.dataUpdatedAt;
    metadata.set(metadataKey(event.query.queryKey), {
      family,
      lastSyncedAt: updatedAt,
      softExpiresAt: updatedAt + getOfflineReadSoftTtl(family),
      retainedUntil: updatedAt + OFFLINE_READ_RETENTION_MS,
      isStale: false,
    });
    schedulePersistence();
  });
  unsubscribeNetwork = NetInfo.addEventListener((state) => {
    const online = networkIsOnline(state);
    if (online && wasOnline === false && currentOwnerMatches(owner)) {
      void client.invalidateQueries({ predicate: (query) => getOfflineReadFamily(query.queryKey) !== null });
    }
    wasOnline = online;
  });
}

export function getOfflineReadMetadata(queryKey: QueryKey, now = Date.now()): OfflineReadMetadata | null {
  const value = metadata.get(metadataKey(queryKey));
  return value ? { ...value, isStale: now > value.softExpiresAt } : null;
}

export function hasOfflineReadDataForRoutes(routes: readonly string[]): boolean {
  return getAvailableOfflineReadRoutes(routes).length > 0;
}

export function getAvailableOfflineReadRoutes(routes: readonly string[]): string[] {
  if (!activeClient) return [];
  const availableRoots = new Set(activeClient.getQueryCache().getAll()
    .filter((query) => query.state.data !== undefined).map((query) => String(query.queryKey[0])));
  return routes.filter((route) => availableRoots.has(route === '/(app)/marketplace'
    ? 'marketplace-suppliers' : route === '/(app)/schedules' ? 'schedules' : 'production-recipes'));
}

export async function clearActiveOfflineReadCache(owner: OfflineCacheOwner | null): Promise<void> {
  if (!owner) {
    stopLifecycle();
    activeClient?.clear();
    activeClient = null;
    activeOwner = null;
    await removeAllOfflineReadCaches();
    return;
  }
  const matches = !!owner && currentOwnerMatches(owner);
  if (matches) {
    stopLifecycle();
    activeClient?.clear();
    activeClient = null;
    activeOwner = null;
  }
  await removeOfflineReadCache(owner);
}

export function resetOfflineReadCacheForTests(): void {
  stopLifecycle();
  activeClient?.clear();
  activeClient = null;
  activeOwner = null;
  writeChain = Promise.resolve();
}
