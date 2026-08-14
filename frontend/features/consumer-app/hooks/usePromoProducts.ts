"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { discountsApi } from "../api/discounts";
import type { CreateDiscountRequest } from "../types";

export const PROMO_PRODUCTS_KEY = ["promo-products"] as const;

/**
 * GET /api/discounts?storeId for the selected store — every status (TASK-525: no `status`
 * filter anymore), so the page's Активні/Минулі/Чернетки tabs can bucket client-side from a
 * single fetch instead of re-querying per tab.
 */
export function usePromoProducts(storeId: string | null) {
  return useQuery({
    queryKey: [...PROMO_PRODUCTS_KEY, storeId],
    queryFn: () => discountsApi.getAll({ storeId: storeId! }),
    enabled: !!storeId,
  });
}

/**
 * POST /api/discounts (reason="promo"). When `publishImmediately` is true (TASK-525's toggle,
 * default ON) this immediately follows with PUT .../approve — same behavior as before this
 * task, since this admin surface otherwise has no separate approval UI. When false, the
 * discount is left in "pending" (shows up in the Чернетки tab) and no approve call is made.
 */
export function useCreatePromoProduct() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      publishImmediately,
      ...body
    }: CreateDiscountRequest & { publishImmediately: boolean }) => {
      const created = await discountsApi.create({ ...body, reason: body.reason ?? "promo" });
      return publishImmediately ? discountsApi.approve(created.id) : created;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: PROMO_PRODUCTS_KEY }),
  });
}

/**
 * PUT /api/discounts/{id}/approve — TASK-525's Чернетки-tab row action, publishing a pending
 * discount outside the create form (same endpoint useCreatePromoProduct's ON path already
 * uses, exposed here as its own mutation).
 */
export function usePublishPromoProduct() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => discountsApi.approve(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: PROMO_PRODUCTS_KEY }),
  });
}

/**
 * PUT /api/discounts/{id}/cancel — "remove from promo" on an active discount, or "discard
 * draft" on a pending one (Discount.Cancel() explicitly allows cancelling from either state).
 * There is no delete endpoint.
 */
export function useCancelPromoProduct() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => discountsApi.cancel(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: PROMO_PRODUCTS_KEY }),
  });
}
