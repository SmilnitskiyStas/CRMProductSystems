import * as SecureStore from 'expo-secure-store';
import {
  clearOfflineSessionSnapshot,
  loadOfflineSessionSnapshot,
  offlineRouteAllowed,
  offlineRoutesFor,
  persistOfflineSessionSnapshot,
} from '../offlineSnapshot';
import type { AuthUser } from '../types';

jest.mock('expo-secure-store', () => ({
  deleteItemAsync: jest.fn(), getItemAsync: jest.fn(), setItemAsync: jest.fn(),
}));

const user: AuthUser = { id: 'u1', tenantId: 't1', role: 'store_manager', email: '', fullName: '',
  locationId: null, permissions: {}, capabilities: [], tabs: ['marketplace', 'schedules', 'production'] };
const settings = { businessType: 'production', modules: ['marketplace', 'production'] };

describe('offline session snapshot', () => {
  beforeEach(() => jest.clearAllMocks());

  test('stores only minimal verified route context and denies nonallowlisted routes', async () => {
    await persistOfflineSessionSnapshot(user, settings, 1_000);
    const raw = (SecureStore.setItemAsync as jest.Mock).mock.calls[0][1];
    expect(raw).not.toMatch(/email|fullName|permissions|capabilities|token/i);
    const snapshot = JSON.parse(raw);
    expect(snapshot.allowedRoutes).toEqual(['/(app)/schedules', '/(app)/marketplace', '/(app)/production/recipes']);
    expect(offlineRouteAllowed('/(app)/marketplace', snapshot)).toBe(true);
    for (const route of ['/(app)', '/(app)/stock', '/(app)/pos', '/(app)/marketplace/p1', '/(app)/production']) {
      expect(offlineRouteAllowed(route, snapshot)).toBe(false);
    }
  });

  test('defaults module-dependent routes to denied', () => {
    expect(offlineRoutesFor(user, { businessType: 'production', modules: [] })).toEqual(['/(app)/schedules']);
  });

  test.each([
    '{broken',
    JSON.stringify({ version: 99 }),
    JSON.stringify({ version: 1, tenantId: 't1', userId: 'u1', role: 'manager', allowedRoutes: [], verifiedAt: 1, expiresAt: 2 }),
  ])('deletes corrupt, incompatible, or expired snapshots', async (raw) => {
    (SecureStore.getItemAsync as jest.Mock).mockResolvedValue(raw);
    await expect(loadOfflineSessionSnapshot(10)).resolves.toEqual({ status: 'invalid' });
    expect(SecureStore.deleteItemAsync).toHaveBeenCalled();
  });

  test('explicit cleanup deletes the protected snapshot', async () => {
    await clearOfflineSessionSnapshot();
    expect(SecureStore.deleteItemAsync).toHaveBeenCalled();
  });
});
