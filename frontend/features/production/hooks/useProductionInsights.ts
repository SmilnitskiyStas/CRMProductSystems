"use client";

import { useQuery } from "@tanstack/react-query";
import { productionInsightsApi } from "../api/production-api";
import { useProductionLocations } from "./useProduction";
import type {
  PlanGranularity,
  ReadyShipmentDto,
  ShippedShipmentDto,
  ShipmentsStatus,
  StockKind,
} from "../insights-types";

export const INSIGHT_KEYS = {
  all: ["production", "insights"] as const,
};

export function useProductionOverview(locationId?: string) {
  return useQuery({
    queryKey: ["production", "insights", "overview", locationId ?? ""],
    queryFn: () => productionInsightsApi.getOverview(locationId),
    staleTime: 15_000,
  });
}

export function useProductionPlan(p: { from: string; to: string; granularity: PlanGranularity; locationId?: string }) {
  return useQuery({
    queryKey: ["production", "insights", "plan", p.from, p.to, p.granularity, p.locationId ?? ""],
    queryFn: () => productionInsightsApi.getPlan(p),
    enabled: !!p.from && !!p.to,
    staleTime: 15_000,
  });
}

export function useProductionShipments<S extends ShipmentsStatus>(p: {
  status: S;
  from?: string;
  to?: string;
  locationId?: string;
}) {
  return useQuery({
    queryKey: ["production", "insights", "shipments", p.status, p.from ?? "", p.to ?? "", p.locationId ?? ""],
    queryFn: () => productionInsightsApi.getShipments(p) as Promise<(ReadyShipmentDto | ShippedShipmentDto)[]>,
    staleTime: 15_000,
  });
}

export function useProductionStock(p: { kind: StockKind; locationId?: string }) {
  return useQuery({
    queryKey: ["production", "insights", "stock", p.kind, p.locationId ?? ""],
    queryFn: () => productionInsightsApi.getStock(p),
    staleTime: 15_000,
  });
}

/** Production-type locations of the tenant (location selects are shown only when > 1). */
export function useProductionLocationList() {
  const q = useProductionLocations();
  const list = (q.data ?? []).filter((l) => l.locationType === "production" && l.isActive !== false);
  return { ...q, list };
}
