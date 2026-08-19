import type { LoyaltyMembershipSummary } from '@/features/loyalty/types';
import { resolveActiveTenantId } from '../reconciliation';

function membership(tenantId: string, status: string = 'active'): LoyaltyMembershipSummary {
  return {
    membershipId: `membership-${tenantId}`,
    tenantId,
    tenantName: tenantId,
    balance: 0,
    status,
    joinedAt: '2026-08-17T00:00:00Z',
    preferredStoreId: null,
    preferredStoreName: null,
    preferredStoreAddress: null,
  };
}

describe('active tenant reconciliation', () => {
  test('keeps a restored tenant that is still an active membership', () => {
    expect(resolveActiveTenantId([membership('a'), membership('b')], 'b')).toBe('b');
  });

  test('falls back when the restored tenant was removed or blocked', () => {
    expect(resolveActiveTenantId([membership('a'), membership('b', 'blocked')], 'b')).toBe('a');
  });

  test('honors a valid retailer explicitly requested by the user', () => {
    expect(resolveActiveTenantId([membership('a'), membership('b')], 'a', 'b')).toBe('b');
  });

  test('clears selection when no active memberships remain', () => {
    expect(resolveActiveTenantId([membership('a', 'blocked')], 'a')).toBeNull();
  });
});
