"use client";

import { useQuery, useMutation } from "@tanstack/react-query";
import { audienceBuilderApi } from "../api/audienceBuilder";
import type {
  AudienceBuildRequest,
  CompetitorAudienceRequest,
  ExportAudienceBuyersRequest,
  ExportCompetitorBuyersRequest,
} from "../types";

const KEY = "audience-builder";

// NOTE: none of the queries below use `placeholderData`/`keepPreviousData` — same reasoning as
// price-segments/RFM: the query key is the full request object, so any filter/exclusion/paging
// change is always a brand-new key. React Query shows a clean loading state instead of quietly
// keeping the previous key's numbers on screen, which matters even more here than in Фаза 1/2
// because the mandatory behavior is "миттєвий перерахунок" (instant recalculation), never a
// lingering stale number next to a just-changed filter.

export function useAudienceCategorySearch(search: string, enabled: boolean) {
  return useQuery({
    queryKey: [KEY, "categories", search],
    queryFn: () => audienceBuilderApi.searchCategories(search, 20),
    enabled: enabled && search.trim().length > 0,
    staleTime: 15_000,
  });
}

// ── Own-product audience ──────────────────────────────────────────────────────────────────────

export function useAudienceOverview(request: AudienceBuildRequest, enabled: boolean) {
  return useQuery({
    queryKey: [KEY, "overview", request],
    queryFn: () => audienceBuilderApi.getOverview(request),
    enabled,
  });
}

export function useAudienceBuyers(request: AudienceBuildRequest, enabled: boolean) {
  return useQuery({
    queryKey: [KEY, "buyers", request],
    queryFn: () => audienceBuilderApi.getBuyers(request),
    enabled,
  });
}

export function useMatchedItems(request: AudienceBuildRequest, enabled: boolean) {
  return useQuery({
    queryKey: [KEY, "matched-items", request],
    queryFn: () => audienceBuilderApi.getMatchedItems(request),
    enabled,
  });
}

export function useExportAudienceBuyers() {
  return useMutation({
    mutationFn: (body: ExportAudienceBuyersRequest) => audienceBuilderApi.exportBuyers(body),
  });
}

// ── Competitor audience ───────────────────────────────────────────────────────────────────────

export function useCompetitorOverview(request: CompetitorAudienceRequest, enabled: boolean) {
  return useQuery({
    queryKey: [KEY, "competitor-overview", request],
    queryFn: () => audienceBuilderApi.getCompetitorOverview(request),
    enabled,
  });
}

export function useCompetitorBuyers(request: CompetitorAudienceRequest, enabled: boolean) {
  return useQuery({
    queryKey: [KEY, "competitor-buyers", request],
    queryFn: () => audienceBuilderApi.getCompetitorBuyers(request),
    enabled,
  });
}

export function useExportCompetitorBuyers() {
  return useMutation({
    mutationFn: (body: ExportCompetitorBuyersRequest) => audienceBuilderApi.exportCompetitorBuyers(body),
  });
}
