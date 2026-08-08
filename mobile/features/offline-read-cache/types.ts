import type { QueryKey } from '@tanstack/react-query';

export interface OfflineCacheOwner {
  tenantId: string;
  userId: string;
}

export type OfflineReadFamily = 'schedules' | 'marketplace-suppliers' | 'production-recipes';

export interface OfflineReadMetadata {
  family: OfflineReadFamily;
  lastSyncedAt: number;
  softExpiresAt: number;
  retainedUntil: number;
  isStale: boolean;
}

export interface PersistedOfflineReadEntry extends OfflineReadMetadata {
  queryKey: QueryKey;
  data: unknown;
}

export interface PersistedOfflineReadCache {
  schemaVersion: number;
  environment: string;
  owner: OfflineCacheOwner;
  savedAt: number;
  entries: PersistedOfflineReadEntry[];
}
