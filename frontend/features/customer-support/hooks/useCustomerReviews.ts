import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { customerReviewsApi } from "../api/reviews";
import type { ReviewFilters } from "../types";

const REVIEWS_KEY = ["customer-support", "reviews"] as const;

export function useCustomerReviews(filters: ReviewFilters = {}) {
  return useQuery({
    queryKey: [...REVIEWS_KEY, "list", filters],
    queryFn: () => customerReviewsApi.getReviews(filters),
    placeholderData: (prev) => prev,
  });
}

export function useReplyToReview() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, replyText }: { id: string; replyText: string }) =>
      customerReviewsApi.reply(id, replyText),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: REVIEWS_KEY }),
  });
}
