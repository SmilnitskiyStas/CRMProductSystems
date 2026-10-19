import { useMutation } from "@tanstack/react-query";
import { receiptsApi } from "@/features/receipts/api/receipts";
import type { OrderLine } from "../types";

export interface ConfirmOrderResult {
  /** Number of receipts (one per supplier) that were created. */
  receipts: number;
  positions: number;
  totalQuantity: number;
}

/**
 * Confirms the picked order lines: creates one draft receipt per supplier (a draft receipt is an
 * "ordered, not yet received" document — the order engine counts it as in-transit). Lines whose
 * product has no default supplier go into one receipt without a supplier. Only the lines the
 * caller passes are ordered — nothing else from the calculation is touched.
 */
export function useConfirmOrder(storeId: string | undefined) {
  return useMutation<ConfirmOrderResult, Error, OrderLine[]>({
    mutationFn: async (lines) => {
      if (!storeId) throw new Error("Store is not selected");
      const toOrder = lines.filter((l) => l.quantityToOrder > 0);
      if (toOrder.length === 0) throw new Error("Nothing to order");

      const groups = new Map<string, OrderLine[]>();
      for (const l of toOrder) {
        const key = l.supplierId ?? "";
        groups.set(key, [...(groups.get(key) ?? []), l]);
      }

      // Sequential on purpose: each POST is its own document, and a failure should leave the
      // earlier ones created but stop (the caller reports it) rather than fan out blindly.
      for (const [supplierId, items] of groups) {
        await receiptsApi.create({
          supplierId: supplierId || null,
          destinationStoreId: storeId,
          viaCentralStore: false,
          expectedAt: null,
          notes: "Автозамовлення (підтверджено на сторінці «Замовлення»)",
          items: items.map((l) => ({
            productId: l.productId,
            quantityOrdered: l.quantityToOrder,
            pricePurchase: l.pricePurchase,
            expiryDate: null,
            batchNumber: null,
          })),
        });
      }

      return {
        receipts: groups.size,
        positions: toOrder.length,
        totalQuantity: toOrder.reduce((s, l) => s + l.quantityToOrder, 0),
      };
    },
  });
}
