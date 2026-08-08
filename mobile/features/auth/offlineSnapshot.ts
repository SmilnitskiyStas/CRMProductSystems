import * as SecureStore from 'expo-secure-store';
import type { AuthUser } from './types';
import type { ModulesSettings } from '@/features/navigation/types';

export const OFFLINE_SESSION_SCHEMA = 1;
export const OFFLINE_SESSION_TTL_MS = 24 * 60 * 60 * 1000;
const KEY = 'offline_read_session_v1';

export type OfflineReadRoute = '/(app)/marketplace' | '/(app)/schedules' | '/(app)/production/recipes';
export interface OfflineSessionSnapshot {
  version: number;
  tenantId: string;
  userId: string;
  role: string;
  allowedRoutes: OfflineReadRoute[];
  verifiedAt: number;
  expiresAt: number;
}

export function offlineRoutesFor(user: AuthUser, settings: ModulesSettings): OfflineReadRoute[] {
  if (!user.tenantId) return [];
  const routes: OfflineReadRoute[] = [];
  if (user.tabs.includes('schedules')) routes.push('/(app)/schedules');
  if (user.tabs.includes('marketplace') && settings.modules.includes('marketplace')) {
    routes.push('/(app)/marketplace');
  }
  const productionRole = ['storekeeper', 'store_manager', 'network_manager', 'enterprise_admin'].includes(user.role);
  if (productionRole && user.tabs.includes('production') && settings.modules.includes('production')
    && ['restaurant', 'production'].includes(settings.businessType)) {
    routes.push('/(app)/production/recipes');
  }
  return routes;
}

export async function persistOfflineSessionSnapshot(user: AuthUser, settings: ModulesSettings, now = Date.now()) {
  if (!user.tenantId) return;
  const snapshot: OfflineSessionSnapshot = { version: OFFLINE_SESSION_SCHEMA, tenantId: user.tenantId,
    userId: user.id, role: user.role, allowedRoutes: offlineRoutesFor(user, settings),
    verifiedAt: now, expiresAt: now + OFFLINE_SESSION_TTL_MS };
  await SecureStore.setItemAsync(KEY, JSON.stringify(snapshot));
}

export async function readOfflineSessionSnapshot(now = Date.now()): Promise<OfflineSessionSnapshot | null> {
  const result = await loadOfflineSessionSnapshot(now);
  return result.status === 'valid' ? result.snapshot : null;
}

export async function loadOfflineSessionSnapshot(now = Date.now()): Promise<
  { status: 'missing' } | { status: 'invalid' } | { status: 'valid'; snapshot: OfflineSessionSnapshot }
> {
  const raw = await SecureStore.getItemAsync(KEY);
  if (!raw) return { status: 'missing' };
  try {
    const value = JSON.parse(raw) as Partial<OfflineSessionSnapshot>;
    const valid = value.version === OFFLINE_SESSION_SCHEMA && typeof value.tenantId === 'string'
      && typeof value.userId === 'string' && typeof value.role === 'string'
      && typeof value.verifiedAt === 'number' && typeof value.expiresAt === 'number'
      && value.expiresAt > now && Array.isArray(value.allowedRoutes)
      && value.allowedRoutes.every((route) => ['/(app)/marketplace','/(app)/schedules','/(app)/production/recipes'].includes(route));
    if (valid) return { status: 'valid', snapshot: value as OfflineSessionSnapshot };
  } catch { /* fail closed below */ }
  await clearOfflineSessionSnapshot();
  return { status: 'invalid' };
}

export async function clearOfflineSessionSnapshot(): Promise<void> {
  await SecureStore.deleteItemAsync(KEY);
}

export function offlineRouteAllowed(route: string, snapshot: OfflineSessionSnapshot): boolean {
  return snapshot.allowedRoutes.includes(route as OfflineReadRoute);
}
