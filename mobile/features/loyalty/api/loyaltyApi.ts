import { apiClient, personalApiClient } from '@/lib/api-client';
import type {
  LoyaltyCode,
  LoyaltyLedgerEntry,
  LoyaltyMembershipSummary,
  ManualLoyaltyAdjustRequest,
  PagedResult,
  ResolveLoyaltyCodeResult,
} from '../types';

// ─── Consumer wallet (consumer JWT via personalApiClient — ConsumerLoyaltyController,
// /api/consumer/loyalty) — TASK-497: never the workspace-scoped apiClient. ──────────────────

export async function getMemberships(): Promise<LoyaltyMembershipSummary[]> {
  const { data } = await personalApiClient.get<LoyaltyMembershipSummary[]>('/consumer/loyalty/memberships');
  return data;
}

export async function getLoyaltyCode(tenantId: string): Promise<LoyaltyCode> {
  const { data } = await personalApiClient.get<LoyaltyCode>(`/consumer/loyalty/${tenantId}/code`);
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
