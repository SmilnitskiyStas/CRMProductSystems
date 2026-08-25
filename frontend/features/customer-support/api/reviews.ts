import { api } from "@/lib/api";
import type { PurchaseReviewDto, ReviewsPage, ReviewFilters } from "../types";

export const customerReviewsApi = {
  getReviews(filters: ReviewFilters = {}): Promise<ReviewsPage> {
    const params = new URLSearchParams();
    params.set("page", String(filters.page ?? 1));
    params.set("pageSize", String(filters.pageSize ?? 50));
    if (filters.rating) params.set("rating", String(filters.rating));
    return api.get<ReviewsPage>(`/api/reviews?${params.toString()}`);
  },

  reply(id: string, replyText: string): Promise<PurchaseReviewDto> {
    return api.put<PurchaseReviewDto>(`/api/reviews/${id}/reply`, { replyText });
  },
};
