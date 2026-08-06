"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { postCampaignApi } from "../api/postCampaign";
import type {
  ExportPostCampaignCustomersRequest,
  PostCampaignAnalyzeRequest,
  PostCampaignCustomerTableParams,
  PostCampaignImportParams,
} from "../types";

const KEY = "post-campaign";

// NOTE: none of the report queries below use `placeholderData`/`keepPreviousData` — same
// reasoning as every sibling MarketingAnalytics feature: the query key is the full
// segment/version/filter tuple, so any change is always a brand-new key and React Query shows a
// clean loading state instead of a stale number sitting next to a just-changed filter.

export function usePostCampaignSegments() {
  return useQuery({
    queryKey: [KEY, "segments"],
    queryFn: () => postCampaignApi.listSegments(),
  });
}

export function useImportPostCampaignSegment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (params: PostCampaignImportParams) => postCampaignApi.importSegment(params),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [KEY, "segments"] });
    },
  });
}

export function useAnalyzePostCampaignSegment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ segmentId, body }: { segmentId: string; body: PostCampaignAnalyzeRequest }) =>
      postCampaignApi.analyzeSegment(segmentId, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [KEY, "segments"] });
    },
  });
}

/**
 * `version` is a caller-supplied opaque string (this feature's `analyzedAt` timestamp) folded
 * into every report query key below — required because re-analyzing the SAME segment with a new
 * date range does not change `segmentId`, so without it React Query would never refetch after
 * "Оновити" (see the store's own doc comment for the full reasoning).
 */
export function usePostCampaignSummary(segmentId: string | null, version: string | null, storeIds: string[], enabled: boolean) {
  return useQuery({
    queryKey: [KEY, "summary", segmentId, version, storeIds],
    queryFn: () => postCampaignApi.getSummary(segmentId!, storeIds),
    enabled: enabled && !!segmentId,
  });
}

export function usePostCampaignDailyTurnover(segmentId: string | null, version: string | null, storeIds: string[], enabled: boolean) {
  return useQuery({
    queryKey: [KEY, "daily-turnover", segmentId, version, storeIds],
    queryFn: () => postCampaignApi.getDailyTurnover(segmentId!, storeIds),
    enabled: enabled && !!segmentId,
  });
}

export function usePostCampaignRfmActivity(segmentId: string | null, version: string | null, storeIds: string[], enabled: boolean) {
  return useQuery({
    queryKey: [KEY, "rfm-activity", segmentId, version, storeIds],
    queryFn: () => postCampaignApi.getRfmActivity(segmentId!, storeIds),
    enabled: enabled && !!segmentId,
  });
}

export function usePostCampaignCustomers(
  segmentId: string | null,
  version: string | null,
  params: PostCampaignCustomerTableParams,
  storeIds: string[],
  enabled: boolean,
) {
  return useQuery({
    queryKey: [KEY, "customers", segmentId, version, params, storeIds],
    queryFn: () => postCampaignApi.getCustomers(segmentId!, params, storeIds),
    enabled: enabled && !!segmentId,
  });
}

export function usePostCampaignMigration(segmentId: string | null, version: string | null, storeIds: string[], enabled: boolean) {
  return useQuery({
    queryKey: [KEY, "migration", segmentId, version, storeIds],
    queryFn: () => postCampaignApi.getMigration(segmentId!, storeIds),
    enabled: enabled && !!segmentId,
  });
}

/** Triggered only by the "Пояснити детальніше" button click — never automatically on report load. */
export function useExplainPostCampaign() {
  return useMutation({
    mutationFn: ({ segmentId, storeIds }: { segmentId: string; storeIds: string[] }) =>
      postCampaignApi.explainSegment(segmentId, storeIds),
  });
}

export function useExportPostCampaignCustomers() {
  return useMutation({
    mutationFn: ({ segmentId, body }: { segmentId: string; body: ExportPostCampaignCustomersRequest }) =>
      postCampaignApi.exportCustomers(segmentId, body),
  });
}

export function useExportPostCampaignUnknownTokens() {
  return useMutation({
    mutationFn: (segmentId: string) => postCampaignApi.exportUnknownTokens(segmentId),
  });
}
