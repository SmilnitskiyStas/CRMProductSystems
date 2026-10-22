import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { receiptsApi } from "@/features/receipts/api/receipts";
import { useStoreScopeReady } from "@/lib/useStoreContext";
import type { UpdateItemPayload } from "@/features/receipts/types";

/** Auto-replenishment drafts (TASK-755/756) of one store. The draft receipt itself is the order. */
export function useAutoDrafts(storeId: string | undefined) {
  const ready = useStoreScopeReady();
  return useQuery({
    queryKey: ["receipts", "auto-drafts", storeId],
    queryFn: () =>
      receiptsApi.getAll({
        store_id: storeId,
        source: "auto_replenishment",
        status: "draft",
        pageSize: 200,
      }),
    enabled: ready && !!storeId,
  });
}

export function useAutoDraftActions() {
  const qc = useQueryClient();
  const invalidate = () => qc.invalidateQueries({ queryKey: ["receipts"] });
  return {
    take: useMutation({ mutationFn: (id: string) => receiptsApi.take(id), onSuccess: invalidate }),
    release: useMutation({ mutationFn: (id: string) => receiptsApi.release(id), onSuccess: invalidate }),
    generate: useMutation({
      mutationFn: (storeId: string) => receiptsApi.autoGenerate(storeId),
      onSuccess: invalidate,
    }),
    // A quantity-only edit must resend the item's current quantityReceived (null for these drafts).
    saveQuantity: useMutation({
      mutationFn: (v: { receiptId: string; item: UpdateItemPayload }) =>
        receiptsApi.updateItems(v.receiptId, [v.item]),
      onSuccess: invalidate,
    }),
  };
}
