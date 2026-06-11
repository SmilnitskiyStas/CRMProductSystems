import { useMutation } from "@tanstack/react-query";
import { ordersApi } from "../api/orders";
import type { OrderCalcResult, RecalcAduResult, RecalcBuffersResult } from "../types";

export interface GenerateOrderResult {
  adu: RecalcAduResult;
  buffers: RecalcBuffersResult;
  order: OrderCalcResult;
}

/**
 * Order-day chain (v2-spec §2 rule 1): refresh ADU → rebuild buffers → compute order.
 */
export function useGenerateOrder() {
  return useMutation<GenerateOrderResult, Error, string>({
    mutationFn: async (storeId: string) => {
      const adu = await ordersApi.recalculateAdu(storeId);
      const buffers = await ordersApi.recalculateBuffers(storeId);
      const order = await ordersApi.calculate(storeId);
      return { adu, buffers, order };
    },
  });
}
