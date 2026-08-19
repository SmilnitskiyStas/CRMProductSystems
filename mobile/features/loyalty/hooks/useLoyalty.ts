import { useEffect } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  getLoyaltyCode,
  getAvailableNetworks,
  getLoyaltyHistory,
  getMemberships,
  getMyMembership,
  joinAsStaff,
  joinTenantProgram,
  manualAdjustLoyalty,
  resolveLoyaltyCode,
  setPreferredStore,
} from '../api/loyaltyApi';
import { useLoyaltyUiStore } from '../store';
import type { LoyaltyMembershipSummary } from '../types';

// ─── Consumer wallet ──────────────────────────────────────────────────────────

export function useMemberships(enabled = true) {
  return useQuery({
    queryKey: ['loyalty', 'memberships'],
    queryFn: getMemberships,
    enabled,
  });
}

export function useAvailableNetworks(enabled = true) {
  return useQuery({
    queryKey: ['loyalty', 'networks'],
    queryFn: getAvailableNetworks,
    enabled,
    staleTime: 5 * 60_000,
  });
}

/**
 * Keeps the shared (wallet + history tabs) selectedTenantId in sync with the membership
 * list: defaults to the first membership once loaded, re-picks if the previously-selected
 * one disappears (e.g. blocked/removed), and clears it when the wallet is empty. Returns
 * the current selection for convenience.
 */
export function useAutoSelectMembership(
  memberships: LoyaltyMembershipSummary[] | undefined
): string | null {
  const selectedTenantId = useLoyaltyUiStore((s) => s.selectedTenantId);
  const setSelectedTenantId = useLoyaltyUiStore((s) => s.setSelectedTenantId);

  useEffect(() => {
    // `undefined` means the memberships request has not resolved yet. Clearing the restored
    // selection here races ActiveTenantProvider/LoyaltyTenantBridge and creates an update loop.
    if (memberships === undefined) return;
    if (memberships.length === 0) {
      if (selectedTenantId !== null) setSelectedTenantId(null);
      return;
    }
    if (!selectedTenantId || !memberships.some((m) => m.tenantId === selectedTenantId)) {
      setSelectedTenantId(memberships[0].tenantId);
    }
  }, [memberships, selectedTenantId, setSelectedTenantId]);

  return selectedTenantId;
}

/**
 * Polls the rotating QR/code. `enabled` must reflect BOTH navigation focus (this tab, not
 * just the app) and OS-level foreground state — the code is a security-sensitive rotating
 * secret and must not keep refreshing off-screen or while backgrounded (see
 * mobile/app/(personal)/wallet.tsx, which combines useFocusEffect + AppState into this flag).
 */
export function useLoyaltyCode(tenantId: string | null, enabled: boolean) {
  return useQuery({
    queryKey: ['loyalty', 'consumer-code', tenantId],
    queryFn: () => getLoyaltyCode(tenantId as string),
    enabled: enabled && tenantId !== null,
    refetchInterval: enabled && tenantId !== null ? 22_000 : false,
    staleTime: 15_000,
    retry: (failureCount, error: unknown) => {
      const status = (error as { response?: { status?: number } })?.response?.status;
      return status !== 403 && status !== 409 && failureCount < 1;
    },
  });
}

export function useLoyaltyHistory(tenantId: string | null, page: number, pageSize = 50) {
  return useQuery({
    queryKey: ['loyalty', 'history', tenantId, page, pageSize],
    queryFn: () => getLoyaltyHistory(tenantId as string, page, pageSize),
    enabled: !!tenantId,
  });
}

export function useJoinTenantProgram() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (tenantId: string) => joinTenantProgram(tenantId),
    onSuccess: (membership) => {
      qc.setQueryData<LoyaltyMembershipSummary[]>(['loyalty', 'memberships'], (current) => {
        if (!current) return [membership];
        const withoutCurrent = current.filter((item) => item.tenantId !== membership.tenantId);
        return [...withoutCurrent, membership];
      });
      void qc.invalidateQueries({ queryKey: ['loyalty', 'memberships'] });
    },
  });
}

export function useSetPreferredStore() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ tenantId, storeId }: { tenantId: string; storeId: string }) =>
      setPreferredStore(tenantId, storeId),
    onSuccess: (membership) => {
      qc.setQueryData<LoyaltyMembershipSummary[]>(['loyalty', 'memberships'], (current) =>
        current?.map((item) =>
          item.membershipId === membership.membershipId ? membership : item
        )
      );
      void qc.invalidateQueries({ queryKey: ['loyalty', 'consumer-code', membership.tenantId] });
    },
  });
}

// ─── Staff POS / cabinet ──────────────────────────────────────────────────────

export function useResolveLoyaltyCode() {
  return useMutation({
    mutationFn: (code: string) => resolveLoyaltyCode(code),
  });
}

export function useManualAdjustLoyalty() {
  return useMutation({
    mutationFn: manualAdjustLoyalty,
  });
}

export function useMyMembership() {
  return useQuery({
    queryKey: ['loyalty', 'my-membership'],
    queryFn: getMyMembership,
    retry: false,
  });
}

export function useJoinAsStaff() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: joinAsStaff,
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['loyalty', 'my-membership'] }),
  });
}
