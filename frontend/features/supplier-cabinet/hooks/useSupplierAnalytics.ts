"use client";

// Supplier demand analytics (supplier-portal expansion #7, Phase 6b).
// Server state — React Query only. Same ["supplier", …] namespace as the other
// portal-expansion hooks (useSupplierWarehouses / useSupplierInventory).

import { useQuery } from "@tanstack/react-query";
import { supplierCabinetApi } from "../api/supplier-cabinet-api";

export const SUPPLIER_ANALYTICS_KEYS = {
  range: (from: string | null, to: string | null) =>
    ["supplier", "analytics", from, to] as const,
};

/**
 * GET /api/supplier-cabinet/analytics?from=&to= — demand analytics over the supplier's own
 * marketplace order history. Pass ISO dates ("YYYY-MM-DD"); omit both for the last 30 days.
 * The response echoes the resolved window (`from` / `to`) — the backend caps the range at 366d.
 */
export function useSupplierAnalytics(from: string | null, to: string | null) {
  return useQuery({
    queryKey: SUPPLIER_ANALYTICS_KEYS.range(from, to),
    queryFn: () => supplierCabinetApi.getAnalytics(from ?? undefined, to ?? undefined),
    staleTime: 60_000,
    retry: false,
  });
}
