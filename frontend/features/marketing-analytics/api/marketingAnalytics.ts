import { api } from "@/lib/api";
import { downloadFilePost } from "@/lib/download";
import type {
  MarketingAnalyticsFilters,
  RfmOverviewDto,
  RfmSegmentDetailDto,
  RfmAffinityResultDto,
  RfmBasketResultDto,
  ExplainRfmSegmentResultDto,
  RfmSegmentKey,
  SegmentExportRequest,
  ProductBuyersExportRequest,
  ProductPairBuyersExportRequest,
} from "../types";

/**
 * Builds the shared filter query string every GET below (plus /explain) takes.
 * `period: "custom"` is a frontend-only signal — never sent as a literal; from/to are sent
 * instead (from+to always override period on the backend anyway, per task log 406).
 */
function buildFilterQs(
  filters: MarketingAnalyticsFilters,
  extra?: Record<string, string | number | undefined>,
): string {
  const qs = new URLSearchParams();
  if (filters.period === "custom") {
    if (filters.from) qs.set("from", filters.from);
    if (filters.to) qs.set("to", filters.to);
  } else {
    qs.set("period", filters.period);
  }
  // Repeated param: ?storeIds=a&storeIds=b — omitted entirely (all stores) when empty.
  for (const id of filters.storeIds) qs.append("storeIds", id);
  if (extra) {
    for (const [k, v] of Object.entries(extra)) {
      if (v !== undefined) qs.set(k, String(v));
    }
  }
  const s = qs.toString();
  return s ? `?${s}` : "";
}

function timestamp(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}_${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
}

/** Client-side filename label only (the server doesn't send one we parse) — safe subset of
 * a free-text product name for use in a downloaded filename. */
function slug(s: string): string {
  return s.trim().replace(/\s+/g, "_").replace(/[^\p{L}\p{N}_-]/gu, "").slice(0, 40) || "item";
}

export const marketingAnalyticsApi = {
  getOverview: (filters: MarketingAnalyticsFilters) =>
    api.get<RfmOverviewDto>(`/api/marketing-analytics/overview${buildFilterQs(filters)}`),

  getSegmentDetail: (key: RfmSegmentKey, filters: MarketingAnalyticsFilters) =>
    api.get<RfmSegmentDetailDto>(`/api/marketing-analytics/segments/${key}${buildFilterQs(filters)}`),

  getAffinity: (key: RfmSegmentKey, productName: string, filters: MarketingAnalyticsFilters, limit = 10) =>
    api.get<RfmAffinityResultDto>(
      `/api/marketing-analytics/segments/${key}/products/${encodeURIComponent(productName)}/affinity${buildFilterQs(filters, { limit })}`,
    ),

  getBasket: (key: RfmSegmentKey, productName: string, filters: MarketingAnalyticsFilters, limit = 10) =>
    api.get<RfmBasketResultDto>(
      `/api/marketing-analytics/segments/${key}/products/${encodeURIComponent(productName)}/basket${buildFilterQs(filters, { limit })}`,
    ),

  /** POST, no body — filter travels via query string exactly like the GETs above. Only ever
   * called on explicit button click (never on segment open). */
  explainSegment: (key: RfmSegmentKey, filters: MarketingAnalyticsFilters) =>
    api.post<ExplainRfmSegmentResultDto>(`/api/marketing-analytics/segments/${key}/explain${buildFilterQs(filters)}`),

  exportSegment: (body: SegmentExportRequest) =>
    downloadFilePost(
      "/api/marketing-analytics/exports/segment",
      body,
      `rfm_${body.key}_segment_${timestamp()}.xlsx`,
    ),

  exportProductBuyers: (body: ProductBuyersExportRequest) =>
    downloadFilePost(
      "/api/marketing-analytics/exports/product-buyers",
      body,
      `rfm_${body.key}_product_${slug(body.productName)}_${timestamp()}.xlsx`,
    ),

  exportProductPairBuyers: (body: ProductPairBuyersExportRequest) =>
    downloadFilePost(
      "/api/marketing-analytics/exports/product-pair-buyers",
      body,
      `rfm_${body.key}_pair_${slug(body.productName)}_${slug(body.pairedProductName)}_${timestamp()}.xlsx`,
    ),
};
