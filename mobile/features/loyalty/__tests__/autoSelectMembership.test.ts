import { act, renderHook } from '@testing-library/react-native';
import { useAutoSelectMembership } from '../hooks/useLoyalty';
import { useLoyaltyUiStore } from '../store';
import type { LoyaltyMembershipSummary } from '../types';

const membership: LoyaltyMembershipSummary = {
  membershipId: 'membership-a',
  tenantId: 'tenant-a',
  tenantName: 'Tenant A',
  balance: 0,
  status: 'active',
  joinedAt: '2026-08-17T00:00:00Z',
  preferredStoreId: null,
  preferredStoreName: null,
  preferredStoreAddress: null,
};

describe('useAutoSelectMembership hydration', () => {
  beforeEach(() => useLoyaltyUiStore.setState({ selectedTenantId: null }));

  test('does not clear a restored tenant while memberships are unresolved', async () => {
    useLoyaltyUiStore.setState({ selectedTenantId: 'tenant-a' });
    const hook = await renderHook(
      ({ memberships }: { memberships: LoyaltyMembershipSummary[] | undefined }) =>
        useAutoSelectMembership(memberships),
      { initialProps: { memberships: undefined } }
    );

    expect(hook.result.current).toBe('tenant-a');
    expect(useLoyaltyUiStore.getState().selectedTenantId).toBe('tenant-a');

    await hook.rerender({ memberships: [membership] });
    expect(useLoyaltyUiStore.getState().selectedTenantId).toBe('tenant-a');
    await hook.unmount();
  });

  test('clears selection only after an explicitly empty response resolves', async () => {
    useLoyaltyUiStore.setState({ selectedTenantId: 'tenant-a' });
    const hook = await renderHook(() => useAutoSelectMembership([]));
    expect(useLoyaltyUiStore.getState().selectedTenantId).toBeNull();
    await hook.unmount();
  });

  test('same-value writes do not notify subscribers', async () => {
    useLoyaltyUiStore.setState({ selectedTenantId: 'tenant-a' });
    const listener = jest.fn();
    const unsubscribe = useLoyaltyUiStore.subscribe(listener);
    await act(async () => useLoyaltyUiStore.getState().setSelectedTenantId('tenant-a'));
    expect(listener).not.toHaveBeenCalled();
    unsubscribe();
  });
});
