import { api } from "@/lib/api";
import { downloadFilePost } from "@/lib/download";
import type {
  AudienceBuildRequest,
  AudienceBuyerTableDto,
  AudienceCategoryOptionDto,
  AudienceFilterState,
  AudienceOverviewDto,
  AudienceTerm,
  AudienceTermRequest,
  CompetitorAudienceRequest,
  CompetitorBuyerTableDto,
  CompetitorFilterState,
  CompetitorOverviewDto,
  ExportAudienceBuyersRequest,
  ExportCompetitorBuyersRequest,
  MatchedItemsTableDto,
  TablePagingState,
} from "../types";

const BASE = "/api/marketing-analytics/audience-builder";

/** Strips client-only fields (`id`, cached category label/count) right before a request goes over
 * the wire — the server only ever needs `kind` + the one value that matches it. */
export function toTermRequests(terms: AudienceTerm[]): AudienceTermRequest[] {
  return terms.map((t) =>
    t.kind === "Text"
      ? { kind: "Text", text: t.text, categoryId: null }
      : { kind: "Category", text: null, categoryId: t.categoryId },
  );
}

/** `overview`/`buyers`/`matched-items` all take this exact shape — build once, reuse per endpoint
 * with each table's own paging bag. `canViewUnmaskedPii` is always sent `false`: the controller
 * re-resolves it server-side for buyers reads and ignores it entirely for overview/matched-items. */
export function buildAudienceRequest(filter: AudienceFilterState, paging: TablePagingState): AudienceBuildRequest {
  return {
    from: filter.from,
    to: filter.to,
    storeIds: filter.storeIds.length > 0 ? filter.storeIds : null,
    terms: toTermRequests(filter.terms),
    mode: filter.mode,
    minQuantity: filter.minQuantity,
    minAmount: filter.minAmount,
    excludedItemIds: filter.excludedItemIds.length > 0 ? filter.excludedItemIds : null,
    page: paging.page,
    pageSize: paging.pageSize,
    sortBy: paging.sortBy,
    sortDescending: paging.sortDescending,
    canViewUnmaskedPii: false,
  };
}

export function buildExportAudienceRequest(filter: AudienceFilterState, unmaskPii: boolean): ExportAudienceBuyersRequest {
  return {
    from: filter.from,
    to: filter.to,
    storeIds: filter.storeIds.length > 0 ? filter.storeIds : null,
    terms: toTermRequests(filter.terms),
    mode: filter.mode,
    minQuantity: filter.minQuantity,
    minAmount: filter.minAmount,
    excludedItemIds: filter.excludedItemIds.length > 0 ? filter.excludedItemIds : null,
    unmaskPii,
  };
}

export function buildCompetitorRequest(filter: CompetitorFilterState, paging: TablePagingState): CompetitorAudienceRequest {
  return {
    from: filter.from,
    to: filter.to,
    storeIds: filter.storeIds.length > 0 ? filter.storeIds : null,
    ownTerms: toTermRequests(filter.ownTerms),
    ownExcludedItemIds: filter.ownExcludedItemIds.length > 0 ? filter.ownExcludedItemIds : null,
    competitorTerms: toTermRequests(filter.competitorTerms),
    horizon: filter.horizon,
    page: paging.page,
    pageSize: paging.pageSize,
    sortBy: paging.sortBy,
    sortDescending: paging.sortDescending,
    canViewUnmaskedPii: false,
  };
}

export function buildExportCompetitorRequest(filter: CompetitorFilterState, unmaskPii: boolean): ExportCompetitorBuyersRequest {
  return {
    from: filter.from,
    to: filter.to,
    storeIds: filter.storeIds.length > 0 ? filter.storeIds : null,
    ownTerms: toTermRequests(filter.ownTerms),
    ownExcludedItemIds: filter.ownExcludedItemIds.length > 0 ? filter.ownExcludedItemIds : null,
    competitorTerms: toTermRequests(filter.competitorTerms),
    horizon: filter.horizon,
    unmaskPii,
  };
}

function timestamp(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}_${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
}

export const audienceBuilderApi = {
  searchCategories: (search: string, limit: number) => {
    const qs = new URLSearchParams();
    if (search.trim()) qs.set("search", search.trim());
    qs.set("limit", String(limit));
    return api.get<AudienceCategoryOptionDto[]>(`${BASE}/categories?${qs.toString()}`);
  },

  // ── Own-product audience ────────────────────────────────────────────────────────────────
  getOverview: (body: AudienceBuildRequest) => api.post<AudienceOverviewDto>(`${BASE}/overview`, body),
  getBuyers: (body: AudienceBuildRequest) => api.post<AudienceBuyerTableDto>(`${BASE}/buyers`, body),
  getMatchedItems: (body: AudienceBuildRequest) => api.post<MatchedItemsTableDto>(`${BASE}/matched-items`, body),
  exportBuyers: (body: ExportAudienceBuyersRequest) =>
    downloadFilePost(`${BASE}/exports/buyers`, body, `audience_buyers_${timestamp()}.xlsx`),

  // ── Competitor audience ─────────────────────────────────────────────────────────────────
  getCompetitorOverview: (body: CompetitorAudienceRequest) => api.post<CompetitorOverviewDto>(`${BASE}/competitor/overview`, body),
  getCompetitorBuyers: (body: CompetitorAudienceRequest) => api.post<CompetitorBuyerTableDto>(`${BASE}/competitor/buyers`, body),
  exportCompetitorBuyers: (body: ExportCompetitorBuyersRequest) =>
    downloadFilePost(`${BASE}/exports/competitor-buyers`, body, `audience_competitor_buyers_${timestamp()}.xlsx`),
};
