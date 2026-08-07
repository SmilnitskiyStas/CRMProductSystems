import { api } from "@/lib/api";
import type { AduDto } from "../types";

// Single-product ADU lookup (TASK-494: days-of-stock-remaining UI). Mirrors backend
// AduController's one read route -- POST /api/adu/recalculate (the controller's only other
// action, a bulk store-wide mutation) is already consumed separately by
// frontend/features/orders/api/orders.ts as part of the order-day ADU→buffers→order chain,
// unrelated to this single-product read.
export const aduApi = {
  get: (storeId: string, productId: string) => api.get<AduDto>(`/api/adu/${storeId}/${productId}`),
};
