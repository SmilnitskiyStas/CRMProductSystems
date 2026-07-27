"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { priceSegmentsApi } from "../api/priceSegments";
import type {
  PriceSegmentsPeriodFilters,
  PriceAudienceKey,
  PriceAudienceTableParams,
  ExportPriceAudienceRequest,
  PriceSegmentKey,
  AllTimeCustomerTableParams,
  ExportAllTimeRequest,
  FrequencyAudienceKey,
  FrequencyAudienceTableParams,
  ExportFrequencyAudienceRequest,
  UpsertPriceSegmentSettingsRequest,
} from "../types";

const KEY = "price-segments";

// NOTE: none of the queries below use `placeholderData`/`keepPreviousData` on purpose — same
// reasoning as `features/marketing-analytics/hooks/useMarketingAnalytics.ts`: query key = the
// full filter/param object, so any filter change is always a brand-new key. React Query shows a
// clean loading state for it instead of quietly keeping the previous key's numbers on screen —
// mandatory per the competitive analysis' §14.5/§23 "never show a mixed old/new state" finding.

// ── Comparison ("Суми покупок") ───────────────────────────────────────────────────────────────

export function usePriceSegmentsOverview(filters: PriceSegmentsPeriodFilters, enabled = true) {
  return useQuery({
    queryKey: [KEY, "overview", filters],
    queryFn: () => priceSegmentsApi.getOverview(filters),
    enabled,
  });
}

export function usePriceAudienceTable(
  params: PriceAudienceTableParams | null,
  filters: PriceSegmentsPeriodFilters,
  enabled = true,
) {
  return useQuery({
    queryKey: [KEY, "audience-table", params, filters],
    queryFn: () => priceSegmentsApi.getAudienceTable(params!, filters),
    enabled: enabled && !!params,
  });
}

/** Triggered only by the "Пояснити детальніше" button click — never automatically on audience open. */
export function useExplainPriceAudience() {
  return useMutation({
    mutationFn: ({ audience, filters }: { audience: PriceAudienceKey; filters: PriceSegmentsPeriodFilters }) =>
      priceSegmentsApi.explainAudience(audience, filters),
  });
}

export function useExportPriceAudience() {
  return useMutation({
    mutationFn: (body: ExportPriceAudienceRequest) => priceSegmentsApi.exportAudience(body),
  });
}

// ── All-time ("Весь час") ─────────────────────────────────────────────────────────────────────

export function useAllTimeOverview(storeIds: string[], selectedSegment: PriceSegmentKey | null, enabled = true) {
  return useQuery({
    queryKey: [KEY, "all-time-overview", storeIds, selectedSegment],
    queryFn: () => priceSegmentsApi.getAllTimeOverview(storeIds, selectedSegment),
    enabled,
  });
}

export function useAllTimeCustomerTable(params: AllTimeCustomerTableParams, storeIds: string[], enabled = true) {
  return useQuery({
    queryKey: [KEY, "all-time-table", params, storeIds],
    queryFn: () => priceSegmentsApi.getAllTimeCustomerTable(params, storeIds),
    enabled,
  });
}

export function useExplainAllTimeSegment() {
  return useMutation({
    mutationFn: ({ segment, storeIds }: { segment: PriceSegmentKey; storeIds: string[] }) =>
      priceSegmentsApi.explainAllTimeSegment(segment, storeIds),
  });
}

export function useExportAllTime() {
  return useMutation({
    mutationFn: (body: ExportAllTimeRequest) => priceSegmentsApi.exportAllTime(body),
  });
}

// ── Frequency & reactivation ───────────────────────────────────────────────────────────────────

export function useFrequencyOverview(
  filters: PriceSegmentsPeriodFilters,
  declineThresholdPercent: number | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: [KEY, "frequency-overview", filters, declineThresholdPercent],
    queryFn: () => priceSegmentsApi.getFrequencyOverview(filters, declineThresholdPercent),
    enabled,
  });
}

export function useFrequencyAudienceTable(
  params: FrequencyAudienceTableParams | null,
  filters: PriceSegmentsPeriodFilters,
  enabled = true,
) {
  return useQuery({
    queryKey: [KEY, "frequency-table", params, filters],
    queryFn: () => priceSegmentsApi.getFrequencyAudienceTable(params!, filters),
    enabled: enabled && !!params,
  });
}

export function useExplainFrequencyAudience() {
  return useMutation({
    mutationFn: ({
      audience,
      filters,
      declineThresholdPercent,
    }: {
      audience: FrequencyAudienceKey;
      filters: PriceSegmentsPeriodFilters;
      declineThresholdPercent: number | undefined;
    }) => priceSegmentsApi.explainFrequencyAudience(audience, filters, declineThresholdPercent),
  });
}

export function useExportFrequencyAudience() {
  return useMutation({
    mutationFn: (body: ExportFrequencyAudienceRequest) => priceSegmentsApi.exportFrequencyAudience(body),
  });
}

// ── Settings (GET/PUT api/settings/price-segments) ─────────────────────────────────────────────

export function usePriceSegmentSettings(enabled = true) {
  return useQuery({
    queryKey: [KEY, "settings"],
    queryFn: () => priceSegmentsApi.getSettings(),
    enabled,
    staleTime: 60_000,
  });
}

export function useUpdatePriceSegmentSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpsertPriceSegmentSettingsRequest) => priceSegmentsApi.updateSettings(body),
    onSuccess: (dto) => {
      queryClient.setQueryData([KEY, "settings"], dto);
    },
  });
}
