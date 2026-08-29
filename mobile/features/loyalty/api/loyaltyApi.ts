import { apiClient, personalApiClient } from '@/lib/api-client';
import type {
  LoyaltyCode,
  LoyaltyLedgerEntry,
  LoyaltyMembershipSummary,
  LoyaltyTierProgress,
  LoyaltyTierDefinition,
  LoyaltyNetworkSummary,
  ManualLoyaltyAdjustRequest,
  PagedResult,
  ResolveLoyaltyCodeResult,
  RetailerPublicInfo,
} from '../types';

// ─── Consumer wallet (consumer JWT via personalApiClient — ConsumerLoyaltyController,
// /api/consumer/loyalty) — TASK-497: never the workspace-scoped apiClient. ──────────────────

export async function getMemberships(): Promise<LoyaltyMembershipSummary[]> {
  const { data } = await personalApiClient.get<LoyaltyMembershipSummary[]>('/consumer/loyalty/memberships');
  return data;
}

export async function getLoyaltyTierProgress(tenantId: string): Promise<LoyaltyTierProgress> {
  return (await personalApiClient.get<LoyaltyTierProgress>(`/consumer/loyalty/${tenantId}/tiers`)).data;
}

/** Consumer-safe public ladder. Requires the backend contract documented in API_GAP.md. */
export async function getLoyaltyTierLadder(tenantId: string): Promise<LoyaltyTierDefinition[]> {
  return (await personalApiClient.get<LoyaltyTierDefinition[]>(`/consumer/loyalty/${tenantId}/tiers/ladder`)).data;
}

export async function getAvailableNetworks(): Promise<LoyaltyNetworkSummary[]> {
  const { data } = await personalApiClient.get<LoyaltyNetworkSummary[]>('/consumer/loyalty/networks');
  return data;
}

export async function getLoyaltyCode(tenantId?: string | null): Promise<LoyaltyCode> {
  const { data } = await personalApiClient.get<LoyaltyCode>('/consumer/loyalty/code', {
    params: tenantId ? { tenantId } : undefined,
  });
  return data;
}

export async function getLoyaltyHistory(
  tenantId: string,
  page = 1,
  pageSize = 50
): Promise<PagedResult<LoyaltyLedgerEntry>> {
  const { data } = await personalApiClient.get<PagedResult<LoyaltyLedgerEntry>>(
    `/consumer/loyalty/${tenantId}/history`,
    { params: { page, pageSize } }
  );
  return data;
}

/**
 * No discovery/browse endpoint exists for "which tenants run a loyalty program" — this is
 * meant to be called with a tenantId the consumer already knows (e.g. told to them in-store).
 * See mobile/app/(personal)/wallet.tsx's join form for the (intentionally minimal, flagged
 * for a future UX pass) manual entry point.
 */
export async function joinTenantProgram(tenantId: string): Promise<LoyaltyMembershipSummary> {
  const { data } = await personalApiClient.post<LoyaltyMembershipSummary>(`/consumer/loyalty/${tenantId}/join`);
  return data;
}

export async function getPublicRetailer(slug: string): Promise<RetailerPublicInfo> {
  const { data } = await personalApiClient.get<RetailerPublicInfo>(
    `/v1/retailers/${encodeURIComponent(slug)}/public`
  );
  return data;
}

export async function joinRetailerBySlug(slug: string): Promise<LoyaltyMembershipSummary> {
  const { data } = await personalApiClient.post<LoyaltyMembershipSummary>(
    `/v1/retailers/${encodeURIComponent(slug)}/join`
  );
  return data;
}

export async function setPreferredStore(
  tenantId: string,
  storeId: string
): Promise<LoyaltyMembershipSummary> {
  const { data } = await personalApiClient.put<LoyaltyMembershipSummary>(
    '/consumer/loyalty/preferred-store',
    { tenantId, storeId }
  );
  return data;
}

// ─── Staff POS / cabinet (staff JWT — LoyaltyController, /api/loyalty) ──────────────────────

export async function resolveLoyaltyCode(code: string): Promise<ResolveLoyaltyCodeResult> {
  const { data } = await apiClient.post<ResolveLoyaltyCodeResult>('/loyalty/resolve-code', { code });
  return data;
}

export async function manualAdjustLoyalty(
  body: ManualLoyaltyAdjustRequest
): Promise<LoyaltyMembershipSummary> {
  const { data } = await apiClient.post<LoyaltyMembershipSummary>('/loyalty/manual-adjust', body);
  return data;
}

/** null when the caller (staff member) has no membership in their own tenant yet. */
export async function getMyMembership(): Promise<LoyaltyMembershipSummary | null> {
  try {
    const { data } = await apiClient.get<LoyaltyMembershipSummary>('/loyalty/my-membership');
    return data;
  } catch (err: unknown) {
    const axiosErr = err as { response?: { status?: number } };
    if (axiosErr?.response?.status === 404) return null;
    throw err;
  }
}

export async function joinAsStaff(): Promise<LoyaltyMembershipSummary> {
  const { data } = await apiClient.post<LoyaltyMembershipSummary>('/loyalty/join-as-staff');
  return data;
}
