import type { LoyaltyMembershipSummary } from './types';

export function selectMembershipForTenant(
  memberships: readonly LoyaltyMembershipSummary[] | undefined,
  tenantId: string | null
): LoyaltyMembershipSummary | null {
  if (!memberships || !tenantId) return null;
  return memberships.find(
    (membership) => membership.tenantId === tenantId && membership.status === 'active'
  ) ?? null;
}
