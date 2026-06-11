import { api } from "@/lib/api";
import type { OrderCalcResult, RecalcAduResult, RecalcBuffersResult } from "../types";

export const ordersApi = {
  recalculateAdu: (storeId: string) =>
    api.post<RecalcAduResult>("/api/adu/recalculate", { storeId }),

  recalculateBuffers: (storeId: string) =>
    api.post<RecalcBuffersResult>("/api/buffer/recalculate", { storeId }),

  calculate: (storeId: string) =>
    api.post<OrderCalcResult>("/api/orders/calculate", { storeId }),
};
