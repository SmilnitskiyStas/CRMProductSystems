"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { discountsApi } from "../api/discounts";
import type { CreateDiscountRequest } from "../types";

export const PROMO_PRODUCTS_KEY = ["promo-products"] as const;

/** GET /api/discounts?storeId&status=active for the selected store. */
export function usePromoProducts(storeId: string | null) {
  return useQuery({
    queryKey: [...PROMO_PRODUCTS_KEY, storeId],
    queryFn: () => discountsApi.getAll({ storeId: storeId!, status: "active" }),
    enabled: !!storeId,
  });
}

/**
 * POST /api/discounts (reason="promo") then immediately PUT .../approve — this admin surface
 * has no separate approval UI anywhere else in the product, so a "add to promo" action here
 * must not leave the item sitting in "pending" (see TASK-521 handoff notes / plan).
 */
export function useCreatePromoProduct() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateDiscountRequest) => {
      const created = await discountsApi.create({ ...body, reason: body.reason ?? "promo" });
      return discountsApi.approve(created.id);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: PROMO_PRODUCTS_KEY }),
  });
}

/** PUT /api/discounts/{id}/cancel — "remove from promo"; there is no delete endpoint. */
export function useCancelPromoProduct() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => discountsApi.cancel(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: PROMO_PRODUCTS_KEY }),
  });
}
