import { api } from "@/lib/api";
import { downloadFilePost } from "@/lib/download";
import type {
  PriceSegmentsPeriodFilters,
  PriceSegmentsOverviewDto,
  PriceAudienceTableDto,
  PriceAudienceTableParams,
  PriceAudienceKey,
  ExplainPriceSegmentResultDto,
  ExportPriceAudienceRequest,
  PriceSegmentsAllTimeOverviewDto,
  AllTimeCustomerTableDto,
  AllTimeCustomerTableParams,
  PriceSegmentKey,
  ExportAllTimeRequest,
  FrequencyOverviewDto,
  FrequencyAudienceTableDto,
  FrequencyAudienceTableParams,
  FrequencyAudienceKey,
  ExportFrequencyAudienceRequest,
  PriceSegmentSettingsDto,
  UpsertPriceSegmentSettingsRequest,
} from "../types";

const BASE = "/api/marketing-analytics/price-segments";
const SETTINGS_BASE = "/api/settings/price-segments";

/** Shared by the comparison + frequency GETs (and their /explain siblings) — "custom" is a
 * frontend-only signal, never sent literally; from/to are sent instead (from+to always wins
 * over `period` on the backend anyway, per task log 420). No "all" value exists here on purpose
 * — all-time is a separate mode/endpoint, never a period preset. */
function buildPeriodQs(
  filters: PriceSegmentsPeriodFilters,
  extra?: Record<string, string | number | boolean | undefined>,
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

/** All-time mode has no period concept at all — just stores (+ an optional segment filter). */
function buildStoreQs(storeIds: string[], extra?: Record<string, string | number | boolean | undefined>): string {
  const qs = new URLSearchParams();
  for (const id of storeIds) qs.append("storeIds", id);
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

export const priceSegmentsApi = {
  // ── Comparison ("Суми покупок") ─────────────────────────────────────────────────────────
  getOverview: (filters: PriceSegmentsPeriodFilters) =>
    api.get<PriceSegmentsOverviewDto>(`${BASE}/overview${buildPeriodQs(filters)}`),

  getAudienceTable: (params: PriceAudienceTableParams, filters: PriceSegmentsPeriodFilters) =>
    api.get<PriceAudienceTableDto>(
      `${BASE}/audiences/${params.audience}${buildPeriodQs(filters, {
        page: params.page,
        pageSize: params.pageSize,
        sortBy: params.sortBy,
        sortDescending: params.sortDescending,
      })}`,
    ),

  /** POST, no body — filters travel via query string exactly like the GETs. Only ever called on
   * explicit button click (never automatically on audience open). */
  explainAudience: (audience: PriceAudienceKey, filters: PriceSegmentsPeriodFilters) =>
    api.post<ExplainPriceSegmentResultDto>(`${BASE}/audiences/${audience}/explain${buildPeriodQs(filters)}`),

  exportAudience: (body: ExportPriceAudienceRequest) =>
    downloadFilePost(`${BASE}/exports/audience`, body, `price_audience_${body.audience}_${timestamp()}.xlsx`),

  // ── All-time ("Весь час") ───────────────────────────────────────────────────────────────
  getAllTimeOverview: (storeIds: string[], selectedSegment: PriceSegmentKey | null) =>
    api.get<PriceSegmentsAllTimeOverviewDto>(
      `${BASE}/all-time${buildStoreQs(storeIds, { selectedSegment: selectedSegment ?? undefined })}`,
    ),

  getAllTimeCustomerTable: (params: AllTimeCustomerTableParams, storeIds: string[]) =>
    api.get<AllTimeCustomerTableDto>(
      `${BASE}/all-time/customers${buildStoreQs(storeIds, {
        segment: params.segment ?? undefined,
        page: params.page,
        pageSize: params.pageSize,
        sortBy: params.sortBy,
        sortDescending: params.sortDescending,
      })}`,
    ),

  explainAllTimeSegment: (segment: PriceSegmentKey, storeIds: string[]) =>
    api.post<ExplainPriceSegmentResultDto>(`${BASE}/all-time/segments/${segment}/explain${buildStoreQs(storeIds)}`),

  exportAllTime: (body: ExportAllTimeRequest) =>
    downloadFilePost(`${BASE}/exports/all-time`, body, `price_all_time_${body.segment ?? "all"}_${timestamp()}.xlsx`),

  // ── Frequency & reactivation ────────────────────────────────────────────────────────────
  getFrequencyOverview: (filters: PriceSegmentsPeriodFilters, declineThresholdPercent: number | undefined) =>
    api.get<FrequencyOverviewDto>(`${BASE}/frequency/overview${buildPeriodQs(filters, { declineThresholdPercent })}`),

  getFrequencyAudienceTable: (params: FrequencyAudienceTableParams, filters: PriceSegmentsPeriodFilters) =>
    api.get<FrequencyAudienceTableDto>(
      `${BASE}/frequency/audiences/${params.audience}${buildPeriodQs(filters, {
        declineThresholdPercent: params.declineThresholdPercent,
        minSpend: params.minSpend,
        maxSpend: params.maxSpend,
        priceSegment: params.priceSegmentFilter,
        page: params.page,
        pageSize: params.pageSize,
        sortBy: params.sortBy,
        sortDescending: params.sortDescending,
      })}`,
    ),

  explainFrequencyAudience: (
    audience: FrequencyAudienceKey,
    filters: PriceSegmentsPeriodFilters,
    declineThresholdPercent: number | undefined,
  ) =>
    api.post<ExplainPriceSegmentResultDto>(
      `${BASE}/frequency/audiences/${audience}/explain${buildPeriodQs(filters, { declineThresholdPercent })}`,
    ),

  exportFrequencyAudience: (body: ExportFrequencyAudienceRequest) =>
    downloadFilePost(`${BASE}/exports/frequency-audience`, body, `price_frequency_${body.audience}_${timestamp()}.xlsx`),

  // ── Settings (GET/PUT api/settings/price-segments) ──────────────────────────────────────
  getSettings: () => api.get<PriceSegmentSettingsDto>(SETTINGS_BASE),

  updateSettings: (body: UpsertPriceSegmentSettingsRequest) =>
    api.put<PriceSegmentSettingsDto>(SETTINGS_BASE, body),
};
