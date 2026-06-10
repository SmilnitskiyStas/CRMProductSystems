import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getReceipts, getReceipt, confirmReceipt } from '../api/receiptApi';

export function useReceipts() {
  return useQuery({
    queryKey: ['receipts'],
    queryFn: getReceipts,
  });
}

export function useReceipt(id: string) {
  return useQuery({
    queryKey: ['receipts', id],
    queryFn: () => getReceipt(id),
    enabled: !!id,
  });
}

export function useConfirmReceipt() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => confirmReceipt(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['receipts'] }),
  });
}
