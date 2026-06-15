import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getTransfers,
  getTransfer,
  createTransfer,
  confirmTransfer,
  cancelTransfer,
  getStores,
} from '../api/transferApi';
import type { CreateTransferPayload } from '../types';

export function useTransfers(storeId?: string) {
  return useQuery({
    queryKey: ['transfers', storeId ?? 'all'],
    queryFn: () => getTransfers(storeId),
  });
}

export function useTransfer(id: string) {
  return useQuery({
    queryKey: ['transfer', id],
    queryFn: () => getTransfer(id),
    enabled: !!id,
  });
}

export function useStores() {
  return useQuery({
    queryKey: ['stores'],
    queryFn: getStores,
    staleTime: 5 * 60 * 1000,
  });
}

export function useCreateTransfer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateTransferPayload) => createTransfer(payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['transfers'] }),
  });
}

export function useConfirmTransfer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => confirmTransfer(id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['transfers'] });
      void qc.invalidateQueries({ queryKey: ['write-offs'] });
    },
  });
}

export function useCancelTransfer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => cancelTransfer(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['transfers'] }),
  });
}
