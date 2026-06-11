import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getReceipts, getReceipt, confirmReceipt, processItem } from '../api/receiptApi';

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

export function useProcessItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ receiptId, itemId, quantityReceived }: {
      receiptId: string; itemId: string; quantityReceived: number;
    }) => processItem(receiptId, itemId, quantityReceived),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['receipts'] }),
  });
}
