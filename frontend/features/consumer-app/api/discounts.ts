import { api } from "@/lib/api";
import type { CreateDiscountRequest, DiscountDto } from "../types";

/**
 * TASK-522: minimal frontend client for the pre-existing DiscountsController
 * (`/api/discounts`) — no new backend was built for the "promo products" admin section, it
 * reuses this controller as-is (reason="promo" everywhere this client is used). Scoped to
 * this feature since no other frontend feature calls /api/discounts yet.
 */
export const discountsApi = {
  getAll: (params: { storeId?: string; status?: string }) => {
    const qs = new URLSearchParams();
    if (params.storeId) qs.set("storeId", params.storeId);
    if (params.status) qs.set("status", params.status);
    const q = qs.toString();
    return api.get<DiscountDto[]>(`/api/discounts${q ? `?${q}` : ""}`);
  },

  create: (body: CreateDiscountRequest) => api.post<DiscountDto>("/api/discounts", body),

  approve: (id: string) => api.put<DiscountDto>(`/api/discounts/${id}/approve`),

  cancel: (id: string) => api.put<DiscountDto>(`/api/discounts/${id}/cancel`),
};
