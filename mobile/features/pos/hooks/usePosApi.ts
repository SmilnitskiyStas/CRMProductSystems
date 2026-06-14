import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { useFocusEffect } from 'expo-router';
import {
  getCurrentShift,
  openShift,
  closeShift,
  createSale,
  getStores,
} from '../api/posApi';
import type { SaleRequest } from '../types';

// ─── Current shift (polls every 10 s while screen is focused) ─────────────────

export function useCurrentShift() {
  const query = useQuery({
    queryKey: ['pos', 'shift', 'current'],
    queryFn: getCurrentShift,
    refetchInterval: 10_000,
    staleTime: 5_000,
    retry: 1,
  });

  // Re-enable the refetch interval only while this screen is mounted
  useFocusEffect(
    useCallback(() => {
      query.refetch();
    }, [query.refetch]) // eslint-disable-line react-hooks/exhaustive-deps
  );

  return query;
}

// ─── Open shift mutation ───────────────────────────────────────────────────────

export function useOpenShift() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (storeId: string) => openShift(storeId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['pos', 'shift'] });
    },
  });
}

// ─── Close shift mutation ──────────────────────────────────────────────────────

export function useCloseShift() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: closeShift,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['pos', 'shift'] });
    },
  });
}

// ─── Create sale mutation ──────────────────────────────────────────────────────

export function useSale() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: SaleRequest) => createSale(body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['pos', 'shift'] });
    },
  });
}
