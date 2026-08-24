import { api } from "@/lib/api";
import type { AiOrder, AiOrderItem, AiOrderListItem } from "../types";

export const aiOrdersApi = {
  // Read-list only (TASK-611: widened from singular store_id to repeated storeIds — empty/
  // undefined = all stores). generate/updateItem/accept/reject below stay single-store writes,
  // unchanged per TASK-610's backend contract.
  getList: (storeIds?: string[]) => {
    const qs = new URLSearchParams();
    if (storeIds) for (const id of storeIds) qs.append("storeIds", id);
    const q = qs.toString();
    return api.get<AiOrderListItem[]>(`/api/ai-orders${q ? `?${q}` : ""}`);
  },

  getById: (id: string) => api.get<AiOrder>(`/api/ai-orders/${id}`),

  generate: (storeId: string) =>
    api.post<AiOrder>("/api/ai-orders/generate", { storeId }),

  updateItem: (orderId: string, itemId: string, quantityFinal: number, editReason: string | null) =>
    api.put<AiOrderItem>(`/api/ai-orders/${orderId}/items/${itemId}`, { quantityFinal, editReason }),

  accept: (id: string) => api.post<AiOrder>(`/api/ai-orders/${id}/accept`),
  reject: (id: string) => api.post<AiOrder>(`/api/ai-orders/${id}/reject`),
};
