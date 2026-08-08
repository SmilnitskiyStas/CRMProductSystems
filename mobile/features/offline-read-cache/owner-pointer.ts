import * as SecureStore from 'expo-secure-store';
import type { OfflineCacheOwner } from './types';

const OWNER_POINTER_KEY = 'offline_read_cache_owner_v1';

export async function persistOfflineCacheOwner(owner: OfflineCacheOwner): Promise<void> {
  await SecureStore.setItemAsync(OWNER_POINTER_KEY, JSON.stringify(owner));
}

export async function readOfflineCacheOwner(): Promise<OfflineCacheOwner | null> {
  const raw = await SecureStore.getItemAsync(OWNER_POINTER_KEY);
  if (!raw) return null;
  try {
    const value = JSON.parse(raw) as Partial<OfflineCacheOwner>;
    if (typeof value.tenantId === 'string' && value.tenantId.length > 0
      && typeof value.userId === 'string' && value.userId.length > 0) {
      return { tenantId: value.tenantId, userId: value.userId };
    }
  } catch {
    // Invalid pointers are deleted below so terminal cleanup cannot repeatedly
    // trust or retry a corrupt owner identity.
  }
  await SecureStore.deleteItemAsync(OWNER_POINTER_KEY).catch(() => undefined);
  return null;
}

export async function clearOfflineCacheOwner(): Promise<void> {
  await SecureStore.deleteItemAsync(OWNER_POINTER_KEY);
}
