import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { receiptsApi } from "@/features/receipts/api/receipts";
import type {
  AddReceiptItemPayload, ReceiptDto, UpdateItemPayload, UpdateReceiptHeaderPayload,
} from "@/features/receipts/types";

/** TASK-758: one order (a receipt) opened as a card. All edits are draft-only and owner-only server-side. */
export function useOrderCard(id: string) {
  return useQuery({
    queryKey: ["receipts", id],
    queryFn: () => receiptsApi.getById(id),
    enabled: !!id,
  });
}

export function useOrderCardActions(id: string) {
  const qc = useQueryClient();
  const refresh = (dto?: ReceiptDto) => {
    if (dto && dto.id) qc.setQueryData(["receipts", id], dto);
    qc.invalidateQueries({ queryKey: ["receipts"] });
  };
  return {
    take: useMutation({ mutationFn: () => receiptsApi.take(id), onSuccess: refresh }),
    release: useMutation({ mutationFn: () => receiptsApi.release(id), onSuccess: refresh }),
    send: useMutation({ mutationFn: () => receiptsApi.send(id), onSuccess: refresh }),
    cancel: useMutation({ mutationFn: () => receiptsApi.cancel(id), onSuccess: refresh }),
    header: useMutation({
      mutationFn: (p: UpdateReceiptHeaderPayload) => receiptsApi.updateHeader(id, p),
      onSuccess: refresh,
    }),
    addItem: useMutation({
      mutationFn: (p: AddReceiptItemPayload) => receiptsApi.addItem(id, p),
      onSuccess: refresh,
    }),
    removeItem: useMutation({
      mutationFn: (itemId: string) => receiptsApi.removeItem(id, itemId),
      onSuccess: refresh,
    }),
    // Edits resend the item's current quantityReceived (null on drafts) to satisfy the existing contract.
    updateItem: useMutation({
      mutationFn: (item: UpdateItemPayload) => receiptsApi.updateItems(id, [item]),
      onSuccess: refresh,
    }),
  };
}
