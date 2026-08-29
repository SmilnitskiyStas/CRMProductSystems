import type { LoyaltyMembershipSummary } from '../types';
import { mergeStoreNetworks, selectMembershipForTenant } from '../selection';

function membership(tenantId: string, balance: number, status = 'active'): LoyaltyMembershipSummary {
  return {
    membershipId: `membership-${tenantId}`,
    tenantId,
    tenantName: `Tenant ${tenantId}`,
    balance,
    status,
    joinedAt: '2026-08-17T00:00:00Z',
    preferredStoreId: null,
    preferredStoreName: null,
    preferredStoreAddress: null,
  };
}

describe('retailer-specific loyalty selection', () => {
  const memberships = [membership('a', 10), membership('b', 900)];

  test('returns only the active tenant membership', () => {
    expect(selectMembershipForTenant(memberships, 'a')?.balance).toBe(10);
    expect(selectMembershipForTenant(memberships, 'b')?.balance).toBe(900);
  });

  test('never falls back to another retailer', () => {
    expect(selectMembershipForTenant(memberships, 'missing')).toBeNull();
    expect(selectMembershipForTenant(memberships, null)).toBeNull();
    expect(selectMembershipForTenant([membership('a', 10, 'blocked')], 'a')).toBeNull();
  });
});

test('keeps joined networks and their preferred stores in the home store selector', () => {
  const joined = { ...membership('a', 10), preferredStoreId: 'store-a', preferredStoreName: 'Центр', preferredStoreAddress: 'Хрещатик 1' };
  expect(mergeStoreNetworks([], [joined])).toEqual([expect.objectContaining({
    tenantId: joined.tenantId,
    stores: [{ storeId: 'store-a', storeName: 'Центр', address: 'Хрещатик 1' }],
  })]);
});
