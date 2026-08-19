import type { LoyaltyMembershipSummary } from '@/features/loyalty/types';

export function resolveActiveTenantId(
  memberships: readonly LoyaltyMembershipSummary[],
  restoredTenantId: string | null,
  requestedTenantId: string | null = null
): string | null {
  const activeMemberships = memberships.filter((membership) => membership.status === 'active');
  if (requestedTenantId && activeMemberships.some((item) => item.tenantId === requestedTenantId)) {
    return requestedTenantId;
  }
  if (restoredTenantId && activeMemberships.some((item) => item.tenantId === restoredTenantId)) {
    return restoredTenantId;
  }
  return activeMemberships[0]?.tenantId ?? null;
}
