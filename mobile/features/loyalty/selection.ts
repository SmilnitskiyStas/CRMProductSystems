import type { LoyaltyMembershipSummary, LoyaltyNetworkSummary } from './types';

export function selectMembershipForTenant(
  memberships: readonly LoyaltyMembershipSummary[] | undefined,
  tenantId: string | null
): LoyaltyMembershipSummary | null {
  if (!memberships || !tenantId) return null;
  return memberships.find(
    (membership) => membership.tenantId === tenantId && membership.status === 'active'
  ) ?? null;
}

export function mergeStoreNetworks(
  networks: readonly LoyaltyNetworkSummary[] | undefined,
  memberships: readonly LoyaltyMembershipSummary[] | undefined,
): LoyaltyNetworkSummary[] {
  const merged = new Map((networks ?? []).map((network) => [network.tenantId, network]));
  for (const membership of memberships ?? []) {
    const current = merged.get(membership.tenantId);
    const preferred = membership.preferredStoreId ? {
      storeId: membership.preferredStoreId,
      storeName: membership.preferredStoreName ?? 'Вибраний магазин',
      address: membership.preferredStoreAddress,
    } : null;
    if (!current) {
      merged.set(membership.tenantId, { tenantId: membership.tenantId, tenantName: membership.tenantName, slug: '', stores: preferred ? [preferred] : [] });
    } else if (preferred && !current.stores.some((store) => store.storeId === preferred.storeId)) {
      merged.set(membership.tenantId, { ...current, stores: [preferred, ...current.stores] });
    }
  }
  return [...merged.values()];
}
