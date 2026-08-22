import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  finalizeMarketplaceReceipt,
  getAwaitingReceiptOrders,
  startMarketplaceReceipt,
  updateMarketplaceReceiptItem,
} from '../api/marketplaceOrdersApi';
import type { UpdateReceiptItemRequest } from '../types';

const listKey = ['marketplace-orders', 'awaiting-receipt'] as const;

export function useAwaitingReceiptOrders() {
  return useQuery({ queryKey: listKey, queryFn: getAwaitingReceiptOrders });
}

export function useMarketplaceReceipt(orderId: string) {
  return useQuery({
    queryKey: ['marketplace-orders', orderId, 'receipt'],
    queryFn: () => startMarketplaceReceipt(orderId),
    enabled: Boolean(orderId),
    retry: false,
  });
}

export function useUpdateMarketplaceReceiptItem(orderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ itemId, body }: { itemId: string; body: UpdateReceiptItemRequest }) =>
      updateMarketplaceReceiptItem(orderId, itemId, body),
    onSuccess: (receipt) => queryClient.setQueryData(
      ['marketplace-orders', orderId, 'receipt'],
      receipt,
    ),
  });
}

export function useFinalizeMarketplaceReceipt(orderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => finalizeMarketplaceReceipt(orderId),
    onSuccess: (receipt) => {
      queryClient.setQueryData(['marketplace-orders', orderId, 'receipt'], receipt);
      void queryClient.invalidateQueries({ queryKey: listKey });
    },
  });
}
