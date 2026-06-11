import { api } from "@/lib/api";
import type { AiOrder, AiOrderItem, AiOrderListItem } from "../types";

export const aiOrdersApi = {
  getList: (storeId?: string) =>
    api.get<AiOrderListItem[]>(`/api/ai-orders${storeId ? `?store_id=${storeId}` : ""}`),

  getById: (id: string) => api.get<AiOrder>(`/api/ai-orders/${id}`),

  generate: (storeId: string) =>
    api.post<AiOrder>("/api/ai-orders/generate", { storeId }),

  updateItem: (orderId: string, itemId: string, quantityFinal: number, editReason: string | null) =>
    api.put<AiOrderItem>(`/api/ai-orders/${orderId}/items/${itemId}`, { quantityFinal, editReason }),

  accept: (id: string) => api.post<AiOrder>(`/api/ai-orders/${id}/accept`),
  reject: (id: string) => api.post<AiOrder>(`/api/ai-orders/${id}/reject`),
};
