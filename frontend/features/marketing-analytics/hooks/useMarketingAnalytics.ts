"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { marketingAnalyticsApi } from "../api/marketingAnalytics";
import type {
  MarketingAnalyticsFilters,
  RfmSegmentKey,
  SegmentExportRequest,
  ProductBuyersExportRequest,
  ProductPairBuyersExportRequest,
  ExportStoreMigrationRequest,
} from "../types";

const KEY = "marketing-analytics";

// NOTE: none of the queries below use `placeholderData`/`keepPreviousData` on purpose — the
// brief requires that switching period/stores atomically replaces every number on the page,
// never showing a mix of old and new data. Query key = the full filter object (per the brief),
// so a filter change is always a brand-new key: React Query shows a clean loading state for it
// instead of quietly keeping the previous key's data on screen.

export function useMarketingAnalyticsOverview(filters: MarketingAnalyticsFilters, enabled = true) {
  return useQuery({
    queryKey: [KEY, "overview", filters],
    queryFn: () => marketingAnalyticsApi.getOverview(filters),
    enabled,
  });
}

export function useRfmSegmentDetail(
  key: RfmSegmentKey | null,
  filters: MarketingAnalyticsFilters,
  enabled = true,
) {
  return useQuery({
    queryKey: [KEY, "segment", key, filters],
    queryFn: () => marketingAnalyticsApi.getSegmentDetail(key!, filters),
    enabled: enabled && !!key,
  });
}

export function useRfmAffinity(
  key: RfmSegmentKey | null,
  productName: string | null,
  filters: MarketingAnalyticsFilters,
  enabled = true,
) {
  return useQuery({
    queryKey: [KEY, "affinity", key, productName, filters],
    queryFn: () => marketingAnalyticsApi.getAffinity(key!, productName!, filters),
    enabled: enabled && !!key && !!productName,
  });
}

export function useRfmBasket(
  key: RfmSegmentKey | null,
  productName: string | null,
  filters: MarketingAnalyticsFilters,
  enabled = true,
) {
  return useQuery({
    queryKey: [KEY, "basket", key, productName, filters],
    queryFn: () => marketingAnalyticsApi.getBasket(key!, productName!, filters),
    enabled: enabled && !!key && !!productName,
  });
}

/** Triggered only by the "Пояснити детальніше" button click — never on segment open. */
export function useExplainRfmSegment() {
  return useMutation({
    mutationFn: ({ key, filters }: { key: RfmSegmentKey; filters: MarketingAnalyticsFilters }) =>
      marketingAnalyticsApi.explainSegment(key, filters),
  });
}

export function useExportSegment() {
  return useMutation({
    mutationFn: (body: SegmentExportRequest) => marketingAnalyticsApi.exportSegment(body),
  });
}

export function useExportProductBuyers() {
  return useMutation({
    mutationFn: (body: ProductBuyersExportRequest) => marketingAnalyticsApi.exportProductBuyers(body),
  });
}

export function useExportProductPairBuyers() {
  return useMutation({
    mutationFn: (body: ProductPairBuyersExportRequest) => marketingAnalyticsApi.exportProductPairBuyers(body),
  });
}

// ── Store migration (TASK-503) ────────────────────────────────────────────────────────────

export function useStoreMigration(filters: MarketingAnalyticsFilters, enabled = true) {
  return useQuery({
    queryKey: [KEY, "store-migration", filters],
    queryFn: () => marketingAnalyticsApi.getStoreMigration(filters),
    enabled,
  });
}

export function useStoreMigrationCustomers(filters: MarketingAnalyticsFilters, limit = 100, enabled = true) {
  return useQuery({
    queryKey: [KEY, "store-migration-customers", filters, limit],
    queryFn: () => marketingAnalyticsApi.getStoreMigrationCustomers(filters, limit),
    enabled,
  });
}

export function useExportStoreMigration() {
  return useMutation({
    mutationFn: (body: ExportStoreMigrationRequest) => marketingAnalyticsApi.exportStoreMigration(body),
  });
}
