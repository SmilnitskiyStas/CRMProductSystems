import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getStock, getStockBatch, createStockBatch, verifyBatch } from '../api/stockApi';
import type { CreateStockBatchRequest } from '../types';

export function useStock(params?: { status?: string; storeId?: string; zone_id?: string }) {
  return useQuery({
    queryKey: ['stock', params],
    queryFn: () => getStock(params),
  });
}

export function useStockBatch(id: string) {
  return useQuery({
    queryKey: ['stock', id],
    queryFn: () => getStockBatch(id),
    enabled: !!id,
  });
}

export function useCreateStockBatch() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateStockBatchRequest) => createStockBatch(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['stock'] }),
  });
}

export function useVerifyBatch() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => verifyBatch(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['stock'] }),
  });
}
