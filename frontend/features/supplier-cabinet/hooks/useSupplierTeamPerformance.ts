"use client";

// Supplier team performance (Phase 8, TASK-696). Server state — React Query only.
// Same ["supplier", …] namespace as the other portal-expansion hooks.

import { useQuery } from "@tanstack/react-query";
import { supplierCabinetApi } from "../api/supplier-cabinet-api";

export const SUPPLIER_TEAM_PERFORMANCE_KEYS = {
  range: (from: string | null, to: string | null) =>
    ["supplier", "team-performance", from, to] as const,
  reviews: (userId: string | null) =>
    ["supplier", "team-reviews", userId] as const,
};

/**
 * GET /api/supplier-cabinet/team-performance?from=&to= — per-employee KPIs over the supplier's
 * own marketplace history. Pass ISO dates ("YYYY-MM-DD"); omit both for the last 30 days.
 * The response echoes the resolved window (`from` / `to`) — the backend caps the range at 366d.
 */
export function useSupplierTeamPerformance(from: string | null, to: string | null) {
  return useQuery({
    queryKey: SUPPLIER_TEAM_PERFORMANCE_KEYS.range(from, to),
    queryFn: () =>
      supplierCabinetApi.getTeamPerformance({
        from: from ?? undefined,
        to: to ?? undefined,
      }),
    staleTime: 60_000,
    retry: false,
  });
}

/**
 * GET /api/supplier-cabinet/team/{userId}/reviews — the individual buyer reviews behind one
 * employee's aggregate, newest first. Disabled until a user id is known.
 */
export function useEmployeeReviews(userId: string | null) {
  return useQuery({
    queryKey: SUPPLIER_TEAM_PERFORMANCE_KEYS.reviews(userId),
    queryFn: () => supplierCabinetApi.getEmployeeReviews(userId!),
    enabled: Boolean(userId),
    staleTime: 60_000,
    retry: false,
  });
}
